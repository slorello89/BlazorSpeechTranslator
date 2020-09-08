using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.Net.WebSockets;
using System;
using Microsoft.AspNetCore.SignalR;
using SpeechTranslatorBlazor.Server.Hubs;

namespace SpeechTranslatorBlazor.Server
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;            
        }

        public IConfiguration Configuration { get; }        

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddControllersWithViews();
            services.AddRazorPages();
            services.AddSignalR();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseWebAssemblyDebugging();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseBlazorFrameworkFiles();
            app.UseStaticFiles();
            //app.UsePathBase("/CoolApp");
            app.UseRouting();            
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapRazorPages();
                endpoints.MapControllers();
                endpoints.MapHub<Hubs.TranslationHub>("/TranslationHub");
                var webSocketOptions = new WebSocketOptions()
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(120),
                    ReceiveBufferSize = 640
                };
                
                app.UseWebSockets(webSocketOptions);
                endpoints.Map("/ws", async (context) => {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var hub = (IHubContext<TranslationHub>)app.ApplicationServices.GetService(typeof(IHubContext<TranslationHub>));
                        WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        using (var engine = new TranslationEngine(Configuration, hub))
                        {
                            await engine.ReceiveAudioOnWebSocket(context, webSocket);
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                });
                endpoints.MapFallbackToFile("index.html");
            });
            
        }
    }
}
