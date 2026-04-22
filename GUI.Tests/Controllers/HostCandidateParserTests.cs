// <copyright file="HostCandidateParserTests.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann, George Higbie, & CS 3500 Course Staff + Associates. All rights reserved.
// </copyright>
// Author: Alex Waldmann
// Date: 2026-04-21

using GUI.Components.Controllers;

namespace GUI.Tests.Controllers;

[TestClass]
public sealed class HostCandidateParserTests
{
    [TestMethod]
    public void HostCandidateParser_Parse_CommaSeparated_ReturnsOrderedHosts()
    {
        var result = HostCandidateParser.Parse("10.128.60.12,192.168.1.25");

        CollectionAssert.AreEqual(new[] { "10.128.60.12", "192.168.1.25" }, result.ToList());
    }

    [TestMethod]
    public void HostCandidateParser_Parse_MixedSeparators_DeduplicatesAndTrims()
    {
        var result = HostCandidateParser.Parse(" 10.128.60.12 ; 192.168.1.25,10.128.60.12 localhost ");

        CollectionAssert.AreEqual(new[] { "10.128.60.12", "192.168.1.25", "localhost" }, result.ToList());
    }

    [TestMethod]
    public void HostCandidateParser_Parse_EmptySpec_ReturnsEmptyList()
    {
        var result = HostCandidateParser.Parse("  , ;  \t  ");

        Assert.IsEmpty(result);
    }
}
