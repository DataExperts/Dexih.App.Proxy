using dexih.proxy.Models;

namespace dexih.proxy.Services
{
    public interface IStreams
    {
        void SetDownloadStream(DownloadObject downloadObject);
        DownloadObject GetDownloadStream(string key, string securityKey);
        void RemoveDownloadStream(string key);
    }
}