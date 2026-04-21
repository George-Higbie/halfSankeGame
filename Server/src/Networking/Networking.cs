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

public class SocketState
{
	public readonly Socket TheSocket;

	public const int BufferSize = 8192;

	internal byte[] buffer = new byte[8192];

	internal StringBuilder data = new StringBuilder();

	public readonly long ID;

	private static long nextID = 0L;

	private static object mutexForID = new object();

	public Action<SocketState> OnNetworkAction;

	public string ErrorMessage { get; internal set; }

	public bool ErrorOccurred { get; internal set; }

	public SocketState(Action<SocketState> toCall, Socket s)
	{
		OnNetworkAction = toCall;
		TheSocket = s;
		lock (mutexForID)
		{
			ID = nextID++;
		}
	}

	public string GetData()
	{
		lock (data)
		{
			return data.ToString();
		}
	}

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
	internal class NewConnectionState
	{
		public Action<SocketState> OnNetworkAction { get; }

		public TcpListener listener { get; }

		public NewConnectionState(Action<SocketState> call, TcpListener lstn)
		{
			OnNetworkAction = call;
			listener = lstn;
		}
	}
	public static class Networking
	{
		public static TcpListener StartServer(Action<SocketState> callMe, int port)
		{
			TcpListener tcpListener = new TcpListener(IPAddress.Any, port);
			try
			{
				tcpListener.Start();
				NewConnectionState state = new NewConnectionState(callMe, tcpListener);
				tcpListener.BeginAcceptSocket(AcceptNewClient, state);
			}
			catch (Exception)
			{
			}
			return tcpListener;
		}

		private static void AcceptNewClient(IAsyncResult ar)
		{
			NewConnectionState newConnectionState = (NewConnectionState)ar.AsyncState;
			SocketState socketState = null;
			Socket socket = null;
			try
			{
				socket = newConnectionState.listener.EndAcceptSocket(ar);
				socket.NoDelay = true;
			}
			catch (Exception ex)
			{
				socketState = new SocketState(newConnectionState.OnNetworkAction, socket)
				{
					ErrorOccurred = true,
					ErrorMessage = ex.ToString()
				};
				socketState.OnNetworkAction(socketState);
				return;
			}
			socketState = new SocketState(newConnectionState.OnNetworkAction, socket);
			socketState.OnNetworkAction(socketState);
			try
			{
				newConnectionState.listener.BeginAcceptSocket(AcceptNewClient, newConnectionState);
			}
			catch (Exception ex2)
			{
				socketState = new SocketState(newConnectionState.OnNetworkAction, socket)
				{
					ErrorOccurred = true,
					ErrorMessage = ex2.ToString()
				};
				socketState.OnNetworkAction(socketState);
			}
		}

		public static void StopServer(TcpListener listener)
		{
			try
			{
				listener.Stop();
			}
			catch (Exception)
			{
			}
		}

		public static void ConnectToServer(Action<SocketState> callMe, string hostName, int port)
		{
			SocketState socketState = new SocketState(null, null)
			{
				ErrorOccurred = true
			};
			IPAddress iPAddress = IPAddress.None;
			try
			{
				IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
				bool flag = false;
				IPAddress[] addressList = hostEntry.AddressList;
				foreach (IPAddress iPAddress2 in addressList)
				{
					if (iPAddress2.AddressFamily != AddressFamily.InterNetworkV6)
					{
						flag = true;
						iPAddress = iPAddress2;
						break;
					}
				}
				if (!flag)
				{
					socketState.ErrorMessage = "Invalid address: " + hostName;
					callMe(socketState);
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
					callMe(socketState);
					return;
				}
			}
			Socket socket = new Socket(iPAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
			socket.NoDelay = true;
			SocketState socketState2 = new SocketState(callMe, socket);
			try
			{
				if (!socketState2.TheSocket.BeginConnect(iPAddress, port, ConnectedCallback, socketState2).AsyncWaitHandle.WaitOne(3000, exitContext: true))
				{
					socketState2.TheSocket.Close();
				}
			}
			catch (Exception ex3)
			{
				socketState.ErrorMessage = ex3.ToString();
				callMe(socketState);
			}
		}

		private static void ConnectedCallback(IAsyncResult ar)
		{
			SocketState socketState = (SocketState)ar.AsyncState;
			try
			{
				socketState.TheSocket.EndConnect(ar);
				socketState.TheSocket.NoDelay = true;
			}
			catch (Exception ex)
			{
				socketState.ErrorMessage = ex.ToString();
				socketState.ErrorOccurred = true;
			}
			socketState.OnNetworkAction(socketState);
		}

		public static void GetData(SocketState state)
		{
			try
			{
				Task.Run(() => state.TheSocket.BeginReceive(state.buffer, 0, 8192, SocketFlags.None, ReceiveCallback, state));
			}
			catch (Exception ex)
			{
				state.ErrorOccurred = true;
				state.ErrorMessage = ex.ToString();
				state.OnNetworkAction(state);
			}
		}

		private static void ReceiveCallback(IAsyncResult ar)
		{
			SocketState socketState = (SocketState)ar.AsyncState;
			Socket theSocket = socketState.TheSocket;
			try
			{
				int num = theSocket.EndReceive(ar);
				if (num > 0)
				{
					lock (socketState.data)
					{
						socketState.data.Append(Encoding.UTF8.GetString(socketState.buffer, 0, num).Trim(new char[2] { '\ufeff', '\u200b' }).Replace("\r", ""));
					}
					socketState.OnNetworkAction(socketState);
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
			socketState.OnNetworkAction(socketState);
		}

		public static bool Send(Socket socket, string data)
		{
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
				}
				return false;
			}
		}

		private static void SendCallback(IAsyncResult ar)
		{
			try
			{
				Socket socket = (Socket)ar.AsyncState;
				if (socket.Connected)
				{
					socket.EndSend(ar);
				}
			}
			catch (Exception)
			{
			}
		}

		public static bool SendAndClose(Socket socket, string data)
		{
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
				}
				return false;
			}
		}

		private static void SendAndCloseCallback(IAsyncResult ar)
		{
			try
			{
				Socket socket = (Socket)ar.AsyncState;
				if (socket.Connected)
				{
					socket.EndSend(ar);
					socket.Close();
				}
			}
			catch (Exception)
			{
			}
		}
	}
}
namespace CS3500.Networking
{
	public sealed class NetworkConnection : IDisposable
	{
		private TcpClient _tcpClient = new TcpClient();

		private StreamReader _reader;

		private StreamWriter _writer;

		public bool IsConnected => _tcpClient?.Connected ?? false;

		public NetworkConnection(TcpClient tcpClient)
		{
			_tcpClient = tcpClient;
			if (IsConnected)
			{
				_reader = new StreamReader(_tcpClient.GetStream(), new UTF8Encoding(false));
				_writer = new StreamWriter(_tcpClient.GetStream(), new UTF8Encoding(false))
				{
					AutoFlush = true
				};
			}
		}

		public NetworkConnection()
			: this(new TcpClient())
		{
		}

		public void Connect(string host, int port)
		{
			_tcpClient = new TcpClient();
			_tcpClient.Connect(host, port);
			_reader = new StreamReader(_tcpClient.GetStream(), new UTF8Encoding(false));
			_writer = new StreamWriter(_tcpClient.GetStream(), new UTF8Encoding(false))
			{
				AutoFlush = true
			};
		}

		public void Send(string message)
		{
			if (_writer == null || !IsConnected)
			{
				throw new InvalidOperationException("Not connected.");
			}
			_writer.WriteLine(new StringBuilder(message));
		}

		public string ReadLine()
		{
			if (_reader == null)
			{
				throw new InvalidOperationException("Not connected.");
			}
			return _reader.ReadLine() ?? throw new IOException("stream was closed");
		}

		public void Disconnect()
		{
			_reader?.Dispose();
			_tcpClient?.Close();
		}

		public void Dispose()
		{
			Disconnect();
		}
	}
}
