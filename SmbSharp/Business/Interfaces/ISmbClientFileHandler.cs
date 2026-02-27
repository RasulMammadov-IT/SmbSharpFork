using SmbSharp.Enums;

namespace SmbSharp.Business.Interfaces
{
    /// <summary>
    /// Interface for SMB client file operations using the smbclient command-line tool.
    /// </summary>
    public interface ISmbClientFileHandler
    {
        /// <summary>
        /// 
        /// </summary>
        bool IsSmbClientAvailable();

        /// <summary>
        /// 
        /// </summary>
        IAsyncEnumerable<string> EnumerateFilesAsync(string smbPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<Stream> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<bool> WriteFileAsync(string filePath, Stream stream,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<bool> WriteFileAsync(string filePath, Stream stream,
            FileWriteMode writeMode, CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<bool> CreateDirectoryAsync(string smbPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// 
        /// </summary>
        Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default);
    }
}