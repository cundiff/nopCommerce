using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;
using Nop.Plugin.Misc.HeadlessStorefront.Services;

namespace Nop.Plugin.Misc.HeadlessStorefront.Infrastructure;

public class NopStartup : INopStartup
{
  public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
  {
    services.AddScoped<IHeadlessSessionService, HeadlessSessionService>();
    services.AddScoped<IHeadlessCatalogService, HeadlessCatalogService>();
    services.AddScoped<IHeadlessCartService, HeadlessCartService>();
    services.AddScoped<IHeadlessCustomerCookieService, HeadlessCustomerCookieService>();

    services.AddCors(options =>
    {
      options.AddPolicy(HeadlessStorefrontDefaults.ApiRoutePrefix, policy =>
      {
        policy
          .WithOrigins(
            "http://localhost:3000",
            "http://127.0.0.1:3000")
          .AllowAnyHeader()
          .AllowAnyMethod();
      });
    });
  }

  public void Configure(IApplicationBuilder application)
  {
    application.UseCors(HeadlessStorefrontDefaults.ApiRoutePrefix);
  }

  public int Order => 3000;
}
