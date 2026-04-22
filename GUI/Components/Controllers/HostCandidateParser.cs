// <copyright file="HostCandidateParser.cs" company="Snake PS10">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-21

using System;
using System.Collections.Generic;

namespace GUI.Components.Controllers;

/// <summary>
/// Parses a host specification into ordered, unique host candidates.
/// Supports comma, semicolon, and whitespace separators.
/// </summary>
public static class HostCandidateParser
{
    /// <summary>
    /// Parses <paramref name="hostSpec"/> into an ordered unique host list.
    /// </summary>
    /// <param name="hostSpec">Host specification, potentially containing multiple entries.</param>
    /// <returns>Unique host candidates in first-seen order.</returns>
    public static IReadOnlyList<string> Parse(string hostSpec)
    {
        ArgumentNullException.ThrowIfNull(hostSpec);

        var parts = hostSpec.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var raw in parts)
        {
            var host = raw.Trim();
            if (host.Length == 0)
            {
                continue;
            }

            if (seen.Add(host))
            {
                ordered.Add(host);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Filters out blocked hosts from an existing candidate list.
    /// </summary>
    /// <param name="candidates">Candidate hosts to evaluate.</param>
    /// <param name="blockedHostsSpec">Blocked host specification (comma/semicolon/whitespace separated).</param>
    /// <returns>Candidates with blocked hosts removed.</returns>
    public static IReadOnlyList<string> FilterBlockedHosts(IReadOnlyList<string> candidates, string? blockedHostsSpec)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (string.IsNullOrWhiteSpace(blockedHostsSpec))
        {
            return candidates;
        }

        var blocked = new HashSet<string>(Parse(blockedHostsSpec), StringComparer.OrdinalIgnoreCase);
        if (blocked.Count == 0)
        {
            return candidates;
        }

        var filtered = new List<string>();
        foreach (var host in candidates)
        {
            if (!blocked.Contains(host))
            {
                filtered.Add(host);
            }
        }

        return filtered;
    }
}
