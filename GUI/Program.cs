// <copyright file="Program.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using GUI.Components;
using System.Net;

static int ResolveListenPort()
{
    string? explicitPort = Environment.GetEnvironmentVariable("GUI_BIND_PORT");
    if (int.TryParse(explicitPort, out int guiBindPort))
    {
        return guiBindPort;
    }

    string? urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
    if (!string.IsNullOrWhiteSpace(urls))
    {
        string firstUrl = urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        if (Uri.TryCreate(firstUrl, UriKind.Absolute, out Uri? parsedUrl) && parsedUrl.Port > 0)
        {
            return parsedUrl.Port;
        }
    }

    return 5145;
}

var builder = WebApplication.CreateBuilder(args);
int listenPort = ResolveListenPort();
builder.WebHost.UseStaticWebAssets();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Any, listenPort);
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register game services (Scoped = one instance per Blazor circuit / browser tab)
builder.Services.AddScoped<GUI.Components.Controllers.ScoreDatabaseController>();
builder.Services.AddScoped<GUI.Components.Controllers.NetworkController>();
builder.Services.AddScoped<GUI.Components.Controllers.GameController>(sp =>
{
    var net = sp.GetRequiredService<GUI.Components.Controllers.NetworkController>();
    var scoreDb = sp.GetRequiredService<GUI.Components.Controllers.ScoreDatabaseController>();
    return new GUI.Components.Controllers.GameController(net, scoreDb);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAntiforgery();

app.MapGet("/health", () => Results.Text("ok", "text/plain"));

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();