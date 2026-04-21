// <copyright file="Program.cs" company="Snake PS9">
// Copyright (c) 2026 Alex Waldmann & George Higbie. All rights reserved.
// </copyright>
// Authors: Alex Waldmann, George Higbie
// Date: 2026-04-12

using GUI.Components;

var builder = WebApplication.CreateBuilder(args);

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
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();