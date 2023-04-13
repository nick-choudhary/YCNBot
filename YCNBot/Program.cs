using Azure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Polly;
using System.Security.Cryptography.X509Certificates;
using YCNBot;
using YCNBot.Core;
using YCNBot.Core.Services;
using YCNBot.Data;
using YCNBot.MessageHandlers;
using YCNBot.Services;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

AuthorizationPolicy requireAuthenticatedUserPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AuthorizeFilter(requireAuthenticatedUserPolicy));
});

IServiceCollection services = builder.Services;

services.AddDbContext<YCNBotContext>(options => options.UseSqlServer(builder.Configuration
    .GetConnectionString("YCNBotContext")));

services.AddHttpContextAccessor();

services.AddScoped<OpenAIClientHandler>();
services.AddScoped<AzureOpenAIClientHandler>();

services
    .AddMicrosoftIdentityWebAppAuthentication(builder.Configuration, "AzureAd");

services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
    options.AddPolicy("AgreedToTerms", policy => policy.RequireClaim("AgreedToTerms"));
});

services.AddHttpClient("OpenAIClient", options =>
{
    string? openAIBaseUrl = builder.Configuration["OpenAIBaseUrl"];

    if (openAIBaseUrl != null)
    {
        options.BaseAddress = new Uri(openAIBaseUrl);
    }
})
   .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder
    .OrResult(x => x.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(5, retry => TimeSpan.FromMilliseconds(20)))
    .AddHttpMessageHandler<OpenAIClientHandler>();

services.AddHttpClient("AzureOpenAIClient", options =>
{
    string? openAIBaseUrl = builder.Configuration["AzureOpenAIBaseUrl"];

    if (openAIBaseUrl != null)
    {
        options.BaseAddress = new Uri(openAIBaseUrl);
    }
})
   .AddTransientHttpErrorPolicy(policyBuilder =>
        policyBuilder
    .OrResult(x => x.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    .WaitAndRetryAsync(5, retry => TimeSpan.FromMilliseconds(20)))
    .AddHttpMessageHandler<AzureOpenAIClientHandler>();

services.AddApplicationInsightsTelemetry();

services.AddScoped<IUnitOfWork, UnitOfWork>();

services.AddTransient<IChatService, ChatService>();
services.AddTransient<IChatCompletionService, AzureChatCompletionService>();
services.AddTransient<IChatCompletionService, OpenAIChatCompletionService>();
services.AddTransient<IChatModelPickerService, ChatModelPickerService>();
services.AddTransient<IIdentityService, IdentityService>();
services.AddTransient<IMessageService, MessageService>();
services.AddTransient<IUserAgreedTermsService, UserAgreedTermsService>();
services.AddTransient<IClaimsTransformation, AgreedToTermsClaimTransformation>();

string? azureADCertThumbprint = builder.Configuration["AzureADCertThumbprint"];

if (builder.Environment.IsProduction() && azureADCertThumbprint != null)
{
    using X509Store x509Store = new (StoreLocation.CurrentUser);

    x509Store.Open(OpenFlags.ReadOnly);

    X509Certificate2 x509Certificate = x509Store.Certificates
        .Find(
            X509FindType.FindByThumbprint,
            azureADCertThumbprint,
            validOnly: false)
        .OfType<X509Certificate2>()
        .Single();

    string? azureKeyVaultUri = builder.Configuration["AzureKeyVaultUri"];

    if (azureKeyVaultUri != null)
    {
        object value = builder.Configuration.AddAzureKeyVault(
            new Uri(azureKeyVaultUri),
            new ClientCertificateCredential(
                builder.Configuration["AzureAD:TenantId"],
                builder.Configuration["AzureAD:ClientId"],
                x509Certificate));
    }
}

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UsePathBase("/api");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

app.MapFallbackToFile("index.html"); ;

app.Run();
