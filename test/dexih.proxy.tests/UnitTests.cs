using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Dexih.Utils.MessageHelpers;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Xunit;
using Microsoft.AspNetCore.TestHost;
using Xunit.Abstractions;

namespace dexih.proxy.tests
{
    public class UnitTests
    {
        private TestServer _server;
        private readonly ITestOutputHelper _output;
        private HttpClient Client { get; set; }
        
        private JsonSerializerOptions serializeOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        
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
            var content = new StringContent("{ \"test\": \"worked\" }", Encoding.UTF8, "application/json");
            var result = await Client.PostAsync("/upload/key/json/file.json", content);
            Assert.True(result.IsSuccessStatusCode);

            var returnValue = await JsonSerializer.DeserializeAsync<ReturnValue>(await result.Content.ReadAsStreamAsync(), serializeOptions);
            Assert.Equal(true, returnValue.Success);

            var result2 = await Client.GetAsync("/download/key/json/file.json");
            var value = await result2.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(value);

            Assert.Equal("file.json", result2.Content.Headers.ContentDisposition.FileName);
            Assert.Equal("application/json", result2.Content.Headers.ContentType.ToString());
            Assert.Equal("worked", json.RootElement.GetProperty("test").GetString());
        }
        
        [Fact]
        public async Task SendSimpleCsv()
        {
            var content = new StringContent("col1,col2,col3", Encoding.UTF8, "text/csv");

            var result = await Client.PostAsync("/upload/key/csv/text.csv", content);
            var returnValue = await JsonSerializer.DeserializeAsync<ReturnValue>(await result.Content.ReadAsStreamAsync(), serializeOptions);
            Assert.Equal(true, returnValue.Success);

            var result2 = await Client.GetAsync("/download/key/csv/text.csv");
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

            var result = await Client.PostAsync("/upload/key/file/file.zip", content);
            var returnValue = await JsonSerializer.DeserializeAsync<ReturnValue>(await result.Content.ReadAsStreamAsync(), serializeOptions);
            Assert.Equal(true, returnValue.Success);

            var result2 = await Client.GetAsync("/download/key/file/file.zip");
            var byteResult = await result2.Content.ReadAsByteArrayAsync();

            Assert.Equal("file.zip", result2.Content.Headers.ContentDisposition.FileName);
            Assert.Equal("application/octet-stream", result2.Content.Headers.ContentType.ToString());
            Assert.Equal(bytes, byteResult);
        }
        
        [Fact]
        public async Task SendSimpleAsync()
        {
            var content = new StringContent("{ \"test\": \"worked\" }", Encoding.UTF8, "application/json");
            var uploadTask = Client.PostAsync("/upload/key/json/file.json", content);
            var downloadTask = Client.GetAsync("/download/key/json/file.json");
        
            var results = await Task.WhenAll(uploadTask, downloadTask);
            
            var returnValue = await JsonSerializer.DeserializeAsync<ReturnValue>(await results[0].Content.ReadAsStreamAsync(), serializeOptions);
            Assert.Equal(true, returnValue.Success);
        
            var jsonText = await results[1].Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(jsonText); 
        
            Assert.Equal("file.json", results[1].Content.Headers.ContentDisposition.FileName);
            Assert.Equal("application/json", results[1].Content.Headers.ContentType.ToString());
            Assert.Equal("worked", json.RootElement.GetProperty("test").GetString());
        }
        
        [Theory]
        [InlineData(1_000_000, 2)] //small
        [InlineData(100_000, 20)] //medium size/ concurrency
       // [InlineData(1_000_000_000, 20)] //large size
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
                var content = new ByteArrayContent(byteArray);
                var uploadTask = Client.PostAsync($"/upload/key{i}/file/file.zip", content);
                var downloadTask = Client.GetAsync($"/download/key{i}/file/file.zip");
        
                var results = await Task.WhenAll(uploadTask, downloadTask);
        
                var returnValue = await JsonSerializer.DeserializeAsync<ReturnValue>(await results[0].Content.ReadAsStreamAsync(), serializeOptions);
                Assert.Equal(true, returnValue.Success);
                
                var byteResult = await results[1].Content.ReadAsByteArrayAsync();
        
                Assert.Equal("file.zip", results[1].Content.Headers.ContentDisposition.FileName);
                Assert.Equal("application/octet-stream", results[1].Content.Headers.ContentType.ToString());
                Assert.Equal(byteArray.Length, byteResult.Length);
            });
        
            stopWatch.Stop();
            _output.WriteLine($"Size: {size}, Concurrent: {concurrent}.  Finished in {stopWatch.ElapsedMilliseconds}ms.");
            
        }
    }
}
