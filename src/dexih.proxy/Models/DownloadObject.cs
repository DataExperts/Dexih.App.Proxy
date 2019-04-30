using System;
using System.IO;
using System.Threading.Tasks;

namespace dexih.proxy.Models
{
    public class DownloadObject : IDisposable
    {
        public DownloadObject(string fileName, Stream stream)
        {
            Key = Guid.NewGuid().ToString();
            SecurityKey = Dexih.Utils.Crypto.EncryptString.GenerateRandomKey().Replace("+", "").Replace("/", "");
            AddedDateTime = DateTime.Now;
            DownloadStream = stream;
            FileName = fileName;
        }

        public string Key { get; private set; }
        public string SecurityKey { get; private set; }
        public Stream DownloadStream { get; set; }
        public string FileName { get; private set; }
        public DateTime AddedDateTime { get; private set; }

        /// <summary>
        /// Copy the uploaded stream for download.
        /// </summary>
        /// <param name="stream">stream to copy to</param>
        /// <param name="timeout">seconds to wait</param>
        /// <returns></returns>
        /// <exception cref="TimeoutException"></exception>
        public async Task CopyDownLoadStream(Stream stream, int timeout)
        {
            var count = 0;
            var maxCount = timeout * 10;
            while (DownloadStream == null)
            {
                await Task.Delay(100);
                if (++count > maxCount)
                {
                    throw new TimeoutException("Timeout occurred waiting for download stream");
                }
            }
            await DownloadStream.CopyToAsync(stream);
        }
        
        public void Dispose()
        {
            DownloadStream?.Dispose();
        }
    }
}