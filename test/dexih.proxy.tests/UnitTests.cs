using System;
using System.Diagnostics;
using System.Net.Cache;
using System.Net.Http;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using dexih.proxy;
using Microsoft.AspNetCore.TestHost;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit.Abstractions;

namespace dexih.proxy.tests
{
    public class UnitTests
    {
        private TestServer _server;
        private readonly ITestOutputHelper _output;
        private HttpClient Client { get; set; }
        
        public UnitTests(ITestOutputHelper output)
        {
            SetUpClient();
            this._output = output;
        }
        
        private void SetUpClient()
        {
            _server = new TestServer(WebHost.CreateDefaultBuilder(new string[0])
                .UseStartup<Startup>());

            Client = _server.CreateClient();
        }
        
        [Fact]
        public async Task PingTest()
        {
            var result = await Client.GetAsync("/ping");
            
            Assert.True(result.IsSuccessStatusCode);

            var ping = await result.Content.ReadAsStringAsync();

            Assert.Equal("{ \"status\": \"alive\"}", ping);
        }
        
        [Fact]
        public async Task SendSimpleJson()
        {
            var content = new StringContent("{ test: \"worked\" }", Encoding.UTF8, "application/json");

            var result = await Client.PostAsync("/upload/json/file.json", content);
            Assert.True(result.IsSuccessStatusCode);
            var url = await result.Content.ReadAsStringAsync();

            var result2 = await Client.GetAsync(url);
            var jsonText = await result2.Content.ReadAsStringAsync();
            var json = JObject.Parse(jsonText); 

            Assert.Equal("file.json", result2.Content.Headers.ContentDisposition.FileName);
            Assert.Equal("application/json", result2.Content.Headers.ContentType.ToString());
            Assert.Equal("worked", json["test"]);
        }
        
        [Fact]
        public async Task SendSimpleCsv()
        {
            var content = new StringContent("col1,col2,col3", Encoding.UTF8, "text/csv");

            var result = await Client.PostAsync("/upload/csv/text.csv", content);
            Assert.True(result.IsSuccessStatusCode);
            var url = await result.Content.ReadAsStringAsync();

            var result2 = await Client.GetAsync(url);
            var csvText = await result2.Content.ReadAsStringAsync();

            Assert.Equal("text.csv", result2.Content.Headers.ContentDisposition.FileName);
            Assert.Equal("text/csv", result2.Content.Headers.ContentType.ToString());
            Assert.Equal("col1,col2,col3", csvText);
        }
        
        [Fact]
        public async Task SendSimpleFile()
        {
            var bytes = new byte[] {65, 66, 67, 68};
            var content = new ByteArrayContent(bytes);

            var result = await Client.PostAsync("/upload/file/file.zip", content);
            Assert.True(result.IsSuccessStatusCode);
            var url = await result.Content.ReadAsStringAsync();

            var result2 = await Client.GetAsync(url);
            var byteResult = await result2.Content.ReadAsByteArrayAsync();

            Assert.Equal("file.zip", result2.Content.Headers.ContentDisposition.FileName);
            Assert.Equal("application/octet-stream", result2.Content.Headers.ContentType.ToString());
            Assert.Equal(bytes, byteResult);
        }
        
        [Fact]
        public async Task SendSimpleAsync()
        {
            var result = await Client.GetAsync("/start/json/file.json");
            Assert.True(result.IsSuccessStatusCode);
            var data = await result.Content.ReadAsStringAsync();
            var json = JObject.Parse(data);
            var uploadUrl = json["UploadUrl"].ToString();
            var downloadUrl = json["DownloadUrl"].ToString();

            var content = new StringContent("{ test: \"worked\" }", Encoding.UTF8, "application/json");
            var uploadTask = Client.PostAsync(uploadUrl, content);
            var downloadTask = Client.GetAsync(downloadUrl);

            Task.WaitAll(uploadTask, downloadTask);

            var jsonText = await downloadTask.Result.Content.ReadAsStringAsync();
            var jsonResult = JObject.Parse(jsonText); 

            Assert.Equal("file.json", downloadTask.Result.Content.Headers.ContentDisposition.FileName);
            Assert.Equal("application/json", downloadTask.Result.Content.Headers.ContentType.ToString());
            Assert.Equal("worked", jsonResult["test"]);
        }
        
        [Theory]
        [InlineData(1_000_000, 2)] //small
        [InlineData(1_000_000_000, 20)] //large size
        [InlineData(100_000, 2000)] //large concurrency
        public void SendLargeAsync(long size, long concurrent)
        {
            var stopWatch = Stopwatch.StartNew();
            
            var byteArray = new byte[size];
            for (var i = 0; i < size; i++)
            {
                byteArray[i] = 65;
            }

            Parallel.For(0, concurrent, async i =>
            {
                var result = await Client.GetAsync("/start/file/file.zip");
                Assert.True(result.IsSuccessStatusCode);
                var data = await result.Content.ReadAsStringAsync();
                var json = JObject.Parse(data);
                var uploadUrl = json["UploadUrl"].ToString();
                var downloadUrl = json["DownloadUrl"].ToString();

                var content = new ByteArrayContent(byteArray);
                var uploadTask = Client.PostAsync(uploadUrl, content);
                var downloadTask = Client.GetAsync(downloadUrl);

                await Task.WhenAll(uploadTask, downloadTask);

                var byteResult = await downloadTask.Result.Content.ReadAsByteArrayAsync();

                Assert.Equal("file.zip", downloadTask.Result.Content.Headers.ContentDisposition.FileName);
                Assert.Equal("application/octet-stream", downloadTask.Result.Content.Headers.ContentType.ToString());
                Assert.Equal(byteArray.Length, byteResult.Length);
            });

            stopWatch.Stop();
            _output.WriteLine($"Size: {size}, Concurrent: {concurrent}.  Finished in {stopWatch.ElapsedMilliseconds}ms.");
            
        }
    }
}
