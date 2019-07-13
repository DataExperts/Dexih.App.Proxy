using System;
using System.IO;
using System.Web;
using dexih.proxy.Models;
using dexih.proxy.Services;
using Dexih.Utils.Crypto;
using Dexih.Utils.MessageHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace dexih.proxy
{
    public class Startup
    {
        private int downloadTimeout = 300;
        private int cleanupInterval = 300;
        private string hostName;
        
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
            services.AddSingleton((IStreams) new Streams(cleanupInterval));

        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, IStreams streams)
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
                    .AllowCredentials()
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
            
            app.Run(async (context) =>
            {
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

                    if (segments[1] == "ping")
                    {
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsync("{ \"Status\": \"Alive\"}");
                    }

                    else if (segments[1] == "upload")
                    {
                        var memoryStream = new MemoryStream();
                        var files = context.Request.Form.Files;
                        if (files.Count >= 1)
                        {
                            await files[0].CopyToAsync(memoryStream);
                            memoryStream.Position = 0;
                        }
                        else
                        {
                            throw new Exception("The file upload only supports one file.");
                        }

                        var type = "";
                        var fileName = "";
                        if (segments.Length > 2)
                        {
                            type = segments[2];
                            fileName = segments[3];
                        }
                        else
                        {
                            throw new Exception(
                                $"Use the format {context.Request.Scheme}://{context.Request.Host}/type/fileName");
                        }

                        var downloadObject = new DownloadObject(fileName, memoryStream);
                        streams.SetDownloadStream(downloadObject);
                        var downloadUrl =
                            $"{GetHost()}/{type}/{HttpUtility.UrlEncode(downloadObject.Key)}/{HttpUtility.UrlEncode(downloadObject.SecurityKey)}";
                        await context.Response.WriteAsync(downloadUrl);
                    }

                    // starts an async upload/download
                    else if (segments[1] == "start")
                    {
                        var type = "";
                        var fileName = "";
                        if (segments.Length > 2)
                        {
                            type = segments[2];
                            fileName = segments[3];
                        }
                        else
                        {
                            throw new Exception(
                                $"Use the format {context.Request.Scheme}://{context.Request.Host}/type/fileName");
                        }

                        var downloadObject = new DownloadObject(fileName, null);
                        streams.SetDownloadStream(downloadObject);
                        var downloadUrl =
                            $"{GetHost()}/{type}/{HttpUtility.UrlEncode(downloadObject.Key)}/{HttpUtility.UrlEncode(downloadObject.SecurityKey)}";
                        var uploadUrl =
                            $"{GetHost()}/send/{HttpUtility.UrlEncode(downloadObject.Key)}/{HttpUtility.UrlEncode(downloadObject.SecurityKey)}";
                        var json = new JObject
                        {
                            {"DownloadUrl", downloadUrl},
                            {"UploadUrl", uploadUrl}
                        };
                        await context.Response.WriteAsync(json.ToString());
                    }

                    // sends data to an async upload.
                    else if (segments[1] == "send")
                    {
                        var key = HttpUtility.UrlDecode(segments[2]);
                        var securityKey = HttpUtility.UrlDecode(segments[3]);
                        var downloadObject = streams.GetDownloadStream(key, securityKey);

                        var memoryStream = new MemoryStream();
                        if (context.Request.HasFormContentType)
                        {
                            var files = context.Request.Form.Files;
                            if (files.Count >= 1)
                            {
                                await files[0].CopyToAsync(memoryStream);
                                memoryStream.Position = 0;
                            }
                            else
                            {
                                throw new Exception("The file upload only supports one file.");
                            }
                        }
                        else
                        {
                            await context.Request.Body.CopyToAsync(memoryStream);
//                    memoryStream.Position = 0;
//                    var temp = Encoding.ASCII.GetString(memoryStream.ToArray());
                            memoryStream.Position = 0;
                        }

                        downloadObject.DownloadStream = memoryStream;

                        // await context.Response.WriteAsync("{ \"status\": \"success\"}");
                    }

                    else if (segments.Length >= 4)
                    {
                        var command = segments[1];
                        var key = HttpUtility.UrlDecode(segments[2]);
                        var securityKey = HttpUtility.UrlDecode(segments[3]);

                        try
                        {
                            var downloadStream = streams.GetDownloadStream(key, securityKey);

                            switch (command)
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
                                    throw new ArgumentOutOfRangeException($"The command {command} was not recognized.");
                            }

                            context.Response.StatusCode = 200;

                            if (!string.IsNullOrEmpty(downloadStream.FileName))
                            {
                                context.Response.Headers.Add("Content-Disposition",
                                    "attachment; filename=" + downloadStream.FileName);
                            }

                            await downloadStream.CopyDownLoadStream(context.Response.Body, downloadTimeout);
                            downloadStream.DownloadStream.Close();
                            streams.RemoveDownloadStream(key);
                        }
                        catch (Exception e)
                        {
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = "application/json";

                            var rand = EncryptString.GenerateRandomKey();

                            var returnValue = new ReturnValue(false, "Data reader failed with error: " + e.Message, e);
                            using (var writer = new StreamWriter(context.Response.Body))
                            {
                                var result = Json.SerializeObject(returnValue, rand);
                                await writer.WriteAsync(result);
                                await writer.FlushAsync().ConfigureAwait(false);
                            }
                        }
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
