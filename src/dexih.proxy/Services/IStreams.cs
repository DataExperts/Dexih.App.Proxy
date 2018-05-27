using System;
using System.IO;
using System.Threading.Tasks;
using dexih.proxy.Models;
using Microsoft.CodeAnalysis;

namespace dexih.proxy.Services
{
    public interface IStreams
    {
        void SetDownloadStream(DownloadObject downloadObject);
        DownloadObject GetDownloadStream(string key, string securityKey);
        void RemoveDownloadStream(string key);
    }
}