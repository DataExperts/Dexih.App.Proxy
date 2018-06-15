using System;
using System.Collections.Concurrent;
using System.Timers;
using dexih.proxy.Models;

namespace dexih.proxy.Services
{
    public class Streams: IStreams
    {
        private readonly ConcurrentDictionary<string, DownloadObject> _streams;

        private readonly int _cleanUpInterval; 

        public Streams(int cleanUpInterval = 5000)
        {
            _streams = new ConcurrentDictionary<string, DownloadObject>();
            _cleanUpInterval = cleanUpInterval;
            
            var cleanup = new Timer {Interval = cleanUpInterval};
            cleanup.Elapsed += CleanUpOldStreams;
        }

        private void CleanUpOldStreams(object o, EventArgs args)
        {
            foreach (var downloadObject in _streams)
            {
                if (downloadObject.Value.AddedDateTime.AddMilliseconds(_cleanUpInterval) < DateTime.Now)
                {
                    _streams.TryRemove(downloadObject.Key, out _);
                }
            }
            
            foreach (var downloadObject in _streams)
            {
                if (downloadObject.Value.AddedDateTime.AddMinutes(5) < DateTime.Now)
                {
                    _streams.TryRemove(downloadObject.Key, out _);
                }
            }
        }

        public void SetDownloadStream(DownloadObject downloadObject)
        {
            _streams.TryAdd(downloadObject.Key, downloadObject);
        }

        public DownloadObject GetDownloadStream(string key, string securityKey)
        {
            var downloadObject = _streams[key];
            if (downloadObject == null)
            {
                throw new Exception(
                    "The download could not complete due to missing download stream.  This could be due to a timeout.");
            }
            
            if (securityKey == downloadObject.SecurityKey)
            {
                return downloadObject;
            }

            throw new Exception("The download could not complete due to mismatching security key.");
        }

        public void RemoveDownloadStream(string key)
        {
            if (!_streams.TryRemove(key, out var _))
            {
                throw new Exception(
                    "The download stream could not be removed.  This could be due to a timeout.");
            }
        }

    }

}