using SmbSharp.Enums;

namespace SmbSharp.Business.Interfaces
{
    /// <summary>
    /// Interface for SMB client file operations using the smbclient command-line tool.
    /// </summary>
    public interface ISmbClientFileHandler
    {
        /// <summary>
        /// Determines whether the SMB client required for file operations is available.
        /// </summary>
        /// <returns><c>true</c> if the SMB client is available; otherwise, <c>false</c>.</returns>
        bool IsSmbClientAvailable();

        /// <summary>
        /// Asynchronously enumerates files in the specified SMB directory.
        /// Returned files' pathes will use //Server/Share/RelativePath format
        /// </summary>
        /// <param name="smbPath">The SMB directory path (\\server\share or //server/share).</param>
        /// <param name="searchAllDirectories">
        /// When <c>true</c>, files in all subdirectories are included; otherwise, only files
        /// in the specified directory are returned.
        /// </param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>An asynchronous stream of file paths.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the directory does not exist or is not accessible.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        IAsyncEnumerable<string> EnumerateFilesAsync(
            string smbPath,
            bool searchAllDirectories = true,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether the specified file exists.
        /// </summary>
        /// <param name="filePath">The SMB file path (\\server\share\folder\file.txt or //server/share/folder/file.txt).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the file exists; otherwise, <c>false</c>.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the parent directory does not exist or is not accessible.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Retrieves a stream for the specified file.
        /// </summary>
        /// <param name="filePath">The SMB file path (\\server\share\folder\file.txt or //server/share/folder/file.txt).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A <see cref="Stream"/> containing the file data.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        Task<Stream> GetFileStreamAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the contents of a stream to the specified file.
        /// Throws <see cref="FileAlreadyExistsException"/> if a file already exists at the destination.
        /// </summary>
        /// <param name="filePath">The destination file path (\\server\share\folder\file.txt or //server/share/folder/file.txt).</param>
        /// <param name="stream">The stream containing the file data.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the write succeeded.</returns>
        /// <exception cref="FileAlreadyExistsException">Thrown when a file already exists at the destination path.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// Writes the stream directly to the destination file.  
        /// Does NOT delete the source file and does NOT perform retries.
        /// </remarks>
        Task<bool> WriteFileAsync(string filePath, Stream stream, CancellationToken cancellationToken = default);

        /// <summary>
        /// Deletes the specified file if it exists. Does nothing if the file does not exist.
        /// </summary>
        /// <param name="filePath">The SMB file path (\\server\share\folder\file.txt or //server/share/folder/file.txt).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the file was deleted, <c>false</c> if it did not exist.</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a directory at the specified SMB path.  
        /// Attempts to create all non-existent parent directories starting from the deepest accessible path.  
        /// Throws <see cref="ArgumentException"/> if the path is invalid or not a directory.
        /// </summary>
        /// <param name="smbPath">The directory path (\\server\share\folder or //server/share/folder).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the directory was created successfully or already exists.</returns>
        /// <exception cref="ArgumentException">Thrown when the path is invalid or not a directory.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        Task<bool> CreateDirectoryAsync(string smbPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Determines whether a connection to the specified SMB directory can be established.
        /// </summary>
        /// <param name="directoryPath">The SMB directory path (\\server\share\folder or //server/share/folder).</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns><c>true</c> if the connection can be established; otherwise, <c>false</c>.</returns>
        Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default);
    }
}