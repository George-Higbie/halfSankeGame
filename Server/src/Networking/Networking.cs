// <copyright file="Networking.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Encapsulates the state of an active socket connection, including its buffer,
/// accumulated data, unique ID, and the callback delegate invoked on network events.
/// </summary>
public class SocketState
{
    // ==================== Private Fields ====================

    /// <summary>Dedicated lock object for incrementing the global connection ID counter.</summary>
    private static readonly object _mutexForId = new();

    /// <summary>Global counter used to assign unique, monotonically increasing IDs to connections.</summary>
    private static long _nextID = 0L;

    // ==================== Public Properties ====================

    /// <summary>The underlying socket for this connection. Null only in error-state instances.</summary>
    public readonly Socket? TheSocket;

    /// <summary>Internal receive buffer size (8 KB).</summary>
    public const int BufferSize = 8192;

    /// <summary>Internal receive buffer written to by async receive operations.</summary>
    internal byte[] buffer = new byte[8192];

    /// <summary>Accumulated partial-message data from receive operations.</summary>
    internal StringBuilder data = new StringBuilder();

    /// <summary>Unique identifier for this connection. Assigned atomically during construction.</summary>
    public readonly long ID;

    /// <summary>
    /// Callback invoked by the networking layer after each asynchronous event.
    /// May be null for error-only socket states.
    /// </summary>
    public Action<SocketState>? OnNetworkAction;

    /// <summary>Human-readable description of the error, if any. Null when no error has occurred.</summary>
    public string? ErrorMessage { get; internal set; }

    /// <summary><c>true</c> if an error has occurred on this connection; otherwise <c>false</c>.</summary>
    public bool ErrorOccurred { get; internal set; }

    // ==================== Constructor ====================

    /// <summary>
    /// Initializes a new <see cref="SocketState"/>.
    /// </summary>
    /// <param name="toCall">The callback to invoke on network activity. May be null for error-only states.</param>
    /// <param name="s">The socket for this connection. May be null for error-only states.</param>
    public SocketState(Action<SocketState>? toCall, Socket? s)
    {
        OnNetworkAction = toCall;
        TheSocket = s;
        lock (_mutexForId)
        {
            ID = _nextID++;
        }
    }

    // ==================== Public Methods ====================

    /// <summary>Returns a thread-safe copy of all accumulated data received on this connection.</summary>
    /// <returns>The current accumulated data as a string.</returns>
    public string GetData()
    {
        lock (data)
        {
            return data.ToString();
        }
    }

    /// <summary>Removes a substring from the accumulated data buffer in a thread-safe manner.</summary>
    /// <param name="start">Zero-based index at which to begin removal.</param>
    /// <param name="length">Number of characters to remove.</param>
    public void RemoveData(int start, int length)
    {
        lock (data)
        {
            data.Remove(start, length);
        }
    }
}

namespace NetworkUtil
{
    /// <summary>Internal state bag passed through the BeginAcceptSocket callback chain.</summary>
    internal class NewConnectionState
    {
        /// <summary>The callback to invoke when a new client connects.</summary>
        public Action<SocketState> OnNetworkAction { get; }

        /// <summary>The TCP listener accepting incoming connections.</summary>
        public TcpListener Listener { get; }

        /// <summary>
        /// Initializes a new <see cref="NewConnectionState"/>.
        /// </summary>
        /// <param name="call">The new-connection callback. Must not be null.</param>
        /// <param name="lstn">The active TCP listener. Must not be null.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="call"/> or <paramref name="lstn"/> is null.
        /// </exception>
        public NewConnectionState(Action<SocketState> call, TcpListener lstn)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentNullException.ThrowIfNull(lstn);
            OnNetworkAction = call;
            Listener = lstn;
        }
    }

    /// <summary>
    /// Static networking utility for asynchronous TCP connections, data transfer, and server listening.
    /// All public entry points validate inputs at the boundary and propagate errors through
    /// <see cref="SocketState.ErrorOccurred"/> rather than throwing across async boundaries.
    /// </summary>
    public static class Networking
    {
        // ==================== Public Methods ====================

        /// <summary>
        /// Starts a TCP listener on <paramref name="port"/> and begins accepting client connections.
        /// Each accepted connection triggers <paramref name="callMe"/>.
        /// </summary>
        /// <param name="callMe">Callback invoked for each new connection. Must not be null.</param>
        /// <param name="port">Port number to listen on.</param>
        /// <returns>The started <see cref="TcpListener"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="callMe"/> is null.</exception>
        /// <exception cref="Exception">
        /// Rethrown if the listener fails to bind to <paramref name="port"/>.
        /// The error is also written to <see cref="Console.Error"/> before rethrowing.
        /// </exception>
        public static TcpListener StartServer(Action<SocketState> callMe, int port)
        {
            ArgumentNullException.ThrowIfNull(callMe);
            TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
            try
            {
                tcpListener.Start();
                NewConnectionState state = new NewConnectionState(callMe, tcpListener);
                tcpListener.BeginAcceptSocket(AcceptNewClient, state);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Fatal: unable to start TCP listener on port " + port + ": " + ex.Message);
                throw;
            }
            return tcpListener;
        }

        /// <summary>Stops the given <see cref="TcpListener"/>, swallowing any <see cref="Exception"/> on failure.</summary>
        /// <param name="listener">The listener to stop. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="listener"/> is null.</exception>
        public static void StopServer(TcpListener listener)
        {
            ArgumentNullException.ThrowIfNull(listener);
            try
            {
                listener.Stop();
            }
            catch (Exception ex)
            {
                // Swallowed: stop failures are non-fatal during shutdown.
                Console.Error.WriteLine("Warning: StopServer threw: " + ex.Message);
            }
        }

        /// <summary>
        /// Resolves <paramref name="hostName"/> to an IP address and attempts a TCP connection on
        /// <paramref name="port"/>. If successful <paramref name="callMe"/> is invoked with the new
        /// <see cref="SocketState"/>; if it fails <paramref name="callMe"/> is invoked with an
        /// error-state <see cref="SocketState"/>.
        /// </summary>
        /// <param name="callMe">Callback invoked on completion. Must not be null.</param>
        /// <param name="hostName">Hostname or IP address string to connect to. Must not be null.</param>
        /// <param name="port">Port number to connect on.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="callMe"/> or <paramref name="hostName"/> is null.
        /// </exception>
        public static void ConnectToServer(Action<SocketState> callMe, string hostName, int port)
        {
            ArgumentNullException.ThrowIfNull(callMe);
            ArgumentNullException.ThrowIfNull(hostName);

            SocketState errorState = new SocketState(callMe, null)
            {
                ErrorOccurred = true
            };
            IPAddress iPAddress = IPAddress.None;
            try
            {
                IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
                bool flag = false;
                IPAddress[] addressList = hostEntry.AddressList;
                foreach (IPAddress candidate in addressList)
                {
                    if (candidate.AddressFamily != AddressFamily.InterNetworkV6)
                    {
                        flag = true;
                        iPAddress = candidate;
                        break;
                    }
                }
                if (!flag)
                {
                    errorState.ErrorMessage = "Invalid address: " + hostName;
                    callMe(errorState);
                    return;
                }
            }
            catch (Exception)
            {
                try
                {
                    iPAddress = IPAddress.Parse(hostName);
                }
                catch (Exception)
                {
                    callMe(errorState);
                    return;
                }
            }
            Socket socket = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;
            SocketState socketState2 = new SocketState(callMe, socket);
            try
            {
                if (!socketState2.TheSocket!.BeginConnect(iPAddress, port, ConnectedCallback, socketState2).AsyncWaitHandle.WaitOne(3000, exitContext: true))
                {
                    socketState2.TheSocket.Close();
                }
            }
            catch (Exception ex3)
            {
                errorState.ErrorMessage = ex3.ToString();
                callMe(errorState);
            }
        }

        /// <summary>
        /// Begins an asynchronous receive on <paramref name="state"/>.
        /// On error, sets <see cref="SocketState.ErrorOccurred"/> and invokes the callback.
        /// </summary>
        /// <param name="state">The socket state to read from. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="state"/> is null.</exception>
        public static void GetData(SocketState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            if (state.TheSocket is null)
            {
                return;
            }
            try
            {
                Task.Run(() => state.TheSocket.BeginReceive(state.buffer, 0, 8192, SocketFlags.None, ReceiveCallback, state));
            }
            catch (Exception ex)
            {
                state.ErrorOccurred = true;
                state.ErrorMessage = ex.ToString();
                state.OnNetworkAction?.Invoke(state);
            }
        }

        /// <summary>
        /// Sends <paramref name="data"/> over <paramref name="socket"/> asynchronously.
        /// Returns <c>false</c> and closes the socket if the send fails.
        /// </summary>
        /// <param name="socket">The target socket. Must not be null.</param>
        /// <param name="data">The string to send. Must not be null.</param>
        /// <returns><c>true</c> if the send was enqueued successfully; <c>false</c> on failure.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="socket"/> or <paramref name="data"/> is null.
        /// </exception>
        public static bool Send(Socket socket, string data)
        {
            ArgumentNullException.ThrowIfNull(socket);
            ArgumentNullException.ThrowIfNull(data);
            if (!socket.Connected)
            {
                return false;
            }
            try
            {
                int offset = 0;
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                socket.BeginSend(bytes, offset, bytes.Length, SocketFlags.None, SendCallback, socket);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                catch (Exception)
                {
                    // Socket may already be closed; safe to ignore secondary failure.
                }
                return false;
            }
        }

        /// <summary>
        /// Sends <paramref name="data"/> over <paramref name="socket"/> and then closes the socket.
        /// </summary>
        /// <param name="socket">The target socket. Must not be null.</param>
        /// <param name="data">The string to send. Must not be null.</param>
        /// <returns><c>true</c> if the send was enqueued successfully; <c>false</c> on failure.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="socket"/> or <paramref name="data"/> is null.
        /// </exception>
        public static bool SendAndClose(Socket socket, string data)
        {
            ArgumentNullException.ThrowIfNull(socket);
            ArgumentNullException.ThrowIfNull(data);
            try
            {
                int offset = 0;
                byte[] bytes = Encoding.UTF8.GetBytes(data);
                socket.BeginSend(bytes, offset, bytes.Length, SocketFlags.None, SendAndCloseCallback, socket);
                return true;
            }
            catch (Exception)
            {
                try
                {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                }
                catch (Exception)
                {
                    // Socket may already be closed; safe to ignore secondary failure.
                }
                return false;
            }
        }

        // ==================== Private Helpers ====================

        /// <summary>Callback invoked by BeginAcceptSocket when a new TCP client arrives.</summary>
        private static void AcceptNewClient(IAsyncResult ar)
        {
            NewConnectionState newConnectionState = (NewConnectionState)ar.AsyncState!;
            SocketState? socketState = null;
            Socket? socket = null;
            try
            {
                socket = newConnectionState.Listener.EndAcceptSocket(ar);
                socket.NoDelay = true;
            }
            catch (Exception ex)
            {
                socketState = new SocketState(newConnectionState.OnNetworkAction, socket)
                {
                    ErrorOccurred = true,
                    ErrorMessage = ex.ToString()
                };
                socketState.OnNetworkAction?.Invoke(socketState);
                return;
            }
            socketState = new SocketState(newConnectionState.OnNetworkAction, socket);
            socketState.OnNetworkAction?.Invoke(socketState);
            try
            {
                newConnectionState.Listener.BeginAcceptSocket(AcceptNewClient, newConnectionState);
            }
            catch (Exception ex2)
            {
                socketState = new SocketState(newConnectionState.OnNetworkAction, socket)
                {
                    ErrorOccurred = true,
                    ErrorMessage = ex2.ToString()
                };
                socketState.OnNetworkAction?.Invoke(socketState);
            }
        }

        /// <summary>Callback invoked when a BeginConnect operation completes.</summary>
        private static void ConnectedCallback(IAsyncResult ar)
        {
            SocketState socketState = (SocketState)ar.AsyncState!;
            try
            {
                socketState.TheSocket!.EndConnect(ar);
                socketState.TheSocket.NoDelay = true;
            }
            catch (Exception ex)
            {
                socketState.ErrorMessage = ex.ToString();
                socketState.ErrorOccurred = true;
            }
            socketState.OnNetworkAction?.Invoke(socketState);
        }

        /// <summary>Callback invoked when a BeginReceive operation completes.</summary>
        private static void ReceiveCallback(IAsyncResult ar)
        {
            SocketState socketState = (SocketState)ar.AsyncState!;
            Socket theSocket = socketState.TheSocket!;
            try
            {
                int num = theSocket.EndReceive(ar);
                if (num > 0)
                {
                    lock (socketState.data)
                    {
                        socketState.data.Append(
                            Encoding.UTF8.GetString(socketState.buffer, 0, num)
                            .Trim('\ufeff', '\u200b')
                            .Replace("\r", ""));
                    }
                    socketState.OnNetworkAction?.Invoke(socketState);
                    return;
                }
                socketState.ErrorMessage = "Socket was closed";
                socketState.ErrorOccurred = true;
            }
            catch (Exception ex)
            {
                socketState.ErrorOccurred = true;
                socketState.ErrorMessage = ex.ToString();
            }
            socketState.OnNetworkAction?.Invoke(socketState);
        }

        /// <summary>Callback invoked when a BeginSend operation completes.</summary>
        private static void SendCallback(IAsyncResult ar)
        {
            Socket? socket = ar.AsyncState as Socket;
            if (socket is null) return;
            try
            {
                if (socket.Connected)
                {
                    socket.EndSend(ar);
                }
            }
            catch (Exception)
            {
                // Send completion errors are non-fatal; the next send will detect disconnect.
            }
        }

        /// <summary>Callback invoked when a BeginSend-and-close operation completes.</summary>
        private static void SendAndCloseCallback(IAsyncResult ar)
        {
            Socket? socket = ar.AsyncState as Socket;
            if (socket is null) return;
            try
            {
                if (socket.Connected)
                {
                    socket.EndSend(ar);
                    socket.Close();
                }
            }
            catch (Exception)
            {
                // Send/close errors are non-fatal; the socket may already be closed.
            }
        }
    }
}

namespace CS3500.Networking
{
    /// <summary>
    /// Wraps a <see cref="TcpClient"/> with synchronous send/receive helpers
    /// and implements <see cref="IDisposable"/> for deterministic resource cleanup.
    /// </summary>
    public sealed class NetworkConnection : IDisposable
    {
        // ==================== Private Fields ====================

        private TcpClient _tcpClient = new TcpClient();
        private StreamReader? _reader;
        private StreamWriter? _writer;

        // ==================== Public Properties ====================

        /// <summary><c>true</c> if the underlying TCP socket is connected.</summary>
        public bool IsConnected => _tcpClient?.Connected ?? false;

        // ==================== Constructors ====================

        /// <summary>
        /// Initializes a <see cref="NetworkConnection"/> wrapping an existing <see cref="TcpClient"/>.
        /// If the client is already connected, stream wrappers are created immediately.
        /// </summary>
        /// <param name="tcpClient">The connected (or unconnected) TCP client. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="tcpClient"/> is null.</exception>
        public NetworkConnection(TcpClient tcpClient)
        {
            ArgumentNullException.ThrowIfNull(tcpClient);
            _tcpClient = tcpClient;
            if (IsConnected)
            {
                _reader = new StreamReader(_tcpClient.GetStream(), Encoding.UTF8);
                _writer = new StreamWriter(_tcpClient.GetStream(), Encoding.UTF8)
                {
                    AutoFlush = true
                };
            }
        }

        /// <summary>Initializes a new unconnected <see cref="NetworkConnection"/>.</summary>
        public NetworkConnection()
            : this(new TcpClient())
        {
        }

        // ==================== Public Methods ====================

        /// <summary>
        /// Connects synchronously to <paramref name="host"/>:<paramref name="port"/> and
        /// initializes the stream wrappers.
        /// </summary>
        /// <param name="host">Hostname or IP address. Must not be null.</param>
        /// <param name="port">Port number.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="host"/> is null.</exception>
        public void Connect(string host, int port)
        {
            ArgumentNullException.ThrowIfNull(host);
            _tcpClient = new TcpClient();
            _tcpClient.Connect(host, port);
            _reader = new StreamReader(_tcpClient.GetStream(), Encoding.UTF8);
            _writer = new StreamWriter(_tcpClient.GetStream(), Encoding.UTF8)
            {
                AutoFlush = true
            };
        }

        /// <summary>
        /// Sends <paramref name="message"/> followed by a newline to the remote endpoint.
        /// </summary>
        /// <param name="message">The message to send. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="message"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when not connected.</exception>
        public void Send(string message)
        {
            ArgumentNullException.ThrowIfNull(message);
            if (_writer is null || !IsConnected)
            {
                throw new InvalidOperationException("Not connected.");
            }
            _writer.WriteLine(new StringBuilder(message));
        }

        /// <summary>Reads a single line from the connection.</summary>
        /// <returns>The received line.</returns>
        /// <exception cref="InvalidOperationException">Thrown when not connected.</exception>
        /// <exception cref="IOException">Thrown when the stream is closed before a full line is received.</exception>
        public string ReadLine()
        {
            if (_reader is null)
            {
                throw new InvalidOperationException("Not connected.");
            }
            return _reader.ReadLine() ?? throw new IOException("stream was closed");
        }

        /// <summary>Disposes the reader and closes the TCP connection.</summary>
        public void Disconnect()
        {
            _reader?.Dispose();
            _tcpClient?.Close();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Disconnect();
        }
    }
}
