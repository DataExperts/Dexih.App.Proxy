using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using dexih.proxy.Models;
using Dexih.Utils.MessageHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace dexih.proxy
{
    public class Startup
    {
        private readonly int downloadTimeout = 300;
        private readonly int cleanupInterval = 300;
        private readonly string hostName;

        public async Task<T> GetCacheItem<T>(string key, IMemoryCache memoryCache)
        {
            for (var i = 0; i < 10; i++)
            {

                if (memoryCache.TryGetValue<T>(key, out var value))
                {
                    return value;
                }

                await Task.Delay(1000);
            }

            return default;
        }
        
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            if (env.IsDevelopment())
            {
                // For more details on using the user secret store see http://go.microsoft.com/fwlink/?LinkID=532709
                // builder.AddUserSecrets<Startup>();
            }

            Configuration = builder.Build();

            var appSettings = Configuration.GetSection("AppSettings");

            if (!string.IsNullOrEmpty(appSettings["DownloadTimeout"]))
            {
                downloadTimeout = Convert.ToInt32(appSettings["DownloadTimeout"]);
            }
            if (!string.IsNullOrEmpty(appSettings["CleanUpInterval"]))
            {
                cleanupInterval = Convert.ToInt32(appSettings["CleanUpInterval"]);
            }
            hostName = appSettings["HostName"];
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddMvc().SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

            // Add Cors
            services.AddCors();
            
            services.AddMemoryCache();
            // services.AddSingleton((IStreams) new Streams(cleanupInterval));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IMemoryCache memoryCache, ILogger<Startup> logger)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }
            
            // only allow requests from the original web site.
            app.UseCors(builder =>
            {
                builder.AllowAnyOrigin() //    .WithOrigins(uploadStreams.OriginUrl)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    // .AllowCredentials()
                    .WithHeaders()
                    .WithMethods()
                    .WithOrigins();
            });

            // these headers pass the client ipAddress from proxy servers (such as nginx)
            app.UseForwardedHeaders(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                                   ForwardedHeaders.XForwardedProto,
            }); 

            app.UseHttpsRedirection();
            // app.UseMvc();
            
            var serializeOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            
            app.Run(async (context) =>
            {
                async Task SendFailedResponse(ReturnValue returnValue)
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "application/json";

                    using (var writer = new StreamWriter(context.Response.Body))
                    {
                        await JsonSerializer.SerializeAsync(writer.BaseStream, returnValue, options: serializeOptions);
                    }
                }
                
                string GetHost()
                {
                    if (string.IsNullOrEmpty(hostName))
                    {
                        return $"{context.Request.Scheme}://{context.Request.Host}";
                    }
                    return hostName;
                }

                try
                {
                    var maxRequestBodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                    if (maxRequestBodySize != null)
                    {
                        maxRequestBodySize.MaxRequestBodySize = 1_000_000_000;
                    }

                    var path = context.Request.Path;
                    var segments = path.Value.Split('/');
                    
                    switch (segments[1])
                    {
                        // returns an alive message
                        case "ping":
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{ \"status\": \"alive\"}");
                            break;

                        case "setRaw":
                            try
                            {
                                var key = segments[2];
                                var value = segments[3];
                                memoryCache.Set(key + "-raw", value);
                            }
                            catch (Exception e)
                            {
                                var returnValue = new ReturnValue(false, "Set raw call failed: " + e.Message, e);
                                await SendFailedResponse(returnValue);
                            }

                            break;
                        
                        case "getRaw":
                            try
                            {
                                var key = segments[2];
                                var value = await GetCacheItem<string>(key + "-raw", memoryCache);
                                
                                context.Response.StatusCode = 200;
                                context.Response.ContentType = "text/plain";
                                await context.Response.WriteAsync(value);
                            }
                            catch (Exception e)
                            {
                                var returnValue = new ReturnValue(false, "Set raw call failed: " + e.Message, e);
                                await SendFailedResponse(returnValue);
                            }

                            break;
                        
                        case "download":
                            try
                            {
                                var key2 = segments[2];
                                using (var downloadStream = await GetCacheItem<DownloadObject>(key2, memoryCache))
                                {

                                    if (downloadStream == null)
                                    {
                                        throw new Exception(
                                            "Remote agent call failed, the response key was not found.");
                                    }

                                    switch (downloadStream.Type)
                                    {
                                        case "file":
                                            context.Response.ContentType = "application/octet-stream";
                                            break;
                                        case "csv":
                                            context.Response.ContentType = "text/csv";
                                            break;
                                        case "json":
                                            context.Response.ContentType = "application/json";
                                            break;
                                        default:
                                            throw new ArgumentOutOfRangeException(
                                                $"The type {downloadStream.Type} was not recognized.");
                                    }

                                    context.Response.StatusCode = downloadStream.IsError ? 400 : 200;
                                    if (!string.IsNullOrEmpty(downloadStream.FileName))
                                    {
                                        context.Response.Headers.Add("Content-Disposition",
                                            "attachment; filename=" + downloadStream.FileName);
                                    }

                                    await downloadStream.DownloadStream.CopyToAsync(context.Response.Body, context.RequestAborted);
                                }
                                
                            }
                            catch (Exception e)
                            {
                                var returnValue = new ReturnValue(false, "Proxy error: " + e.Message, e);
                                await SendFailedResponse(returnValue);
                            }

                            break;

                        // starts an async upload
                        case "upload":
                            
                            try
                            {
                                var key = segments[2];
                                var type = "";
                                var fileName = "";

                                if (segments.Length > 3)
                                {
                                    type = segments[3];
                                    if (segments.Length > 4)
                                    {
                                        fileName = segments[4];
                                    }
                                }
                                else
                                {
                                    throw new Exception(
                                        $"Use the format {context.Request.Scheme}://{context.Request.Host}/type/fileName");
                                }

                                Stream stream;
                                if (context.Request.HasFormContentType)
                                {
                                    var files = context.Request.Form.Files;
                                    if (files.Count != 1)
                                    {
                                        throw new Exception("The file upload only supports one file.");
                                    }

                                    stream = files[0].OpenReadStream();
                                }
                                else
                                {
                                    stream = new BufferedStream(context.Request.Body);
                                }
                                
                                var readWriteStream = new ReadWriteStream();
                                var uploadObject = new DownloadObject(fileName, type, readWriteStream, false);
                                await stream.CopyToAsync(readWriteStream);
                                memoryCache.Set(key, uploadObject, TimeSpan.FromSeconds(cleanupInterval));

                                await context.Response.WriteAsync("{\"success\": true}");

                            }
                            catch (Exception e)
                            {
                                var returnValue = new ReturnValue(false, "Proxy error: " + e.Message, e);
                                SendFailedResponse(returnValue);
                            }

                            break;

                        case "error":
                            
                            try
                            {
                                var key = segments[2];

                                var stream = new BufferedStream(context.Request.Body);
                                var readWriteStream = new ReadWriteStream();
                                var uploadObject = new DownloadObject("", "json", readWriteStream, true);
                                memoryCache.Set(key, uploadObject, TimeSpan.FromSeconds(cleanupInterval));

                                await stream.CopyToAsync(readWriteStream);
                                await context.Response.WriteAsync("{\"success\": true}");

                            }
                            catch (Exception e)
                            {
                                var returnValue = new ReturnValue(false, "Proxy error: " + e.Message, e);
                                SendFailedResponse(returnValue);
                            }

                            break;
                    }

                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }

            });
        }
    }
}
