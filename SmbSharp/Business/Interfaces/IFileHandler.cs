namespace SmbSharp.Business.Interfaces
{
    /// <summary>
    /// Provides an interface for file operations on SMB/CIFS shares across different platforms.
    /// Supports both Kerberos authentication and username/password authentication.
    /// </summary>
    public interface IFileHandler
    {
        /// <summary>
        /// Asynchronously enumerates files in the specified directory on an SMB share.
        /// On windows without WSL Returned files' pathes will use \\Server\Share\RelativePath format
        /// In Other cases Returned files' pathes will use //Server/Share/RelativePath format
        /// </summary>
        /// <param name="directory">The SMB directory path.</param>
        /// <param name="searchAllDirectories">
        /// When <c>true</c>, files in all subdirectories are included; otherwise, only files in the specified directory are returned.
        /// </param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>An asynchronous stream of file paths found in the directory.</returns>
        /// <exception cref="DirectoryNotFoundException">
        /// Thrown when the directory does not exist or is not accessible.
        /// </exception>
        /// <exception cref="IOException">
        /// Thrown when the SMB operation fails.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when the operation is cancelled.
        /// </exception>
        /// <remarks>
        /// On Windows systems only UNC paths are accepted.
        /// On non-Windows systems or WSL environments both UNC and Linux paths are accepted.
        /// </remarks>
        IAsyncEnumerable<string> EnumerateFilesAsync(string directory, bool searchAllDirectories = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks whether a file exists at the specified path.
        /// </summary>
        /// <param name="filePath">The full path to the file.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the file exists, otherwise false.</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// On Windows systems only UNC paths are accepted.  
        /// On non-Windows systems or WSL environments both UNC and Linux paths are accepted.
        /// </remarks>
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);


        /// <summary>
        /// Opens a file for reading and returns a stream containing its contents.
        /// The caller is responsible for disposing the returned stream.
        /// </summary>
        /// <param name="filePath">The full path to the file.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>A stream containing the file data.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist or is not accessible.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// On Windows systems only UNC paths are accepted.  
        /// On non-Windows systems or WSL environments both UNC and Linux paths are accepted.
        /// </remarks>
        Task<Stream> ReadFileAsync(string filePath, CancellationToken cancellationToken = default);


        /// <summary>
        /// Writes string content to a file, creating a new file at the specified path.
        /// </summary>
        /// <param name="filePath">The full destination path.</param>
        /// <param name="content">Content to write to the file.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the operation succeeded.</returns>
        /// <exception cref="FileAlreadyExistsException">Thrown when a file already exists at the destination path.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// On Windows systems only UNC paths are accepted.  
        /// On non-Windows systems or WSL environments both UNC and Linux paths are accepted.
        /// </remarks>
        Task<bool> WriteFileAsync(string filePath, string content, CancellationToken cancellationToken = default);

        /// <summary>
        /// Writes the contents of a previously fetched stream to the specified destination file.
        /// </summary>
        /// <param name="filePath">The destination file path.</param>
        /// <param name="stream">The stream containing the file data.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the operation succeeded.</returns>
        /// <exception cref="FileAlreadyExistsException">
        /// Thrown when a file already exists at the destination path.
        /// </exception>
        /// <exception cref="IOException">
        /// Thrown when the SMB write operation fails.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// Thrown when the operation is cancelled.
        /// </exception>
        /// <remarks>
        /// Uses retry logic when writing the stream to the destination file.
        /// The stream is assumed to have been previously fetched from the source location.
        /// </remarks>
        Task<bool> WriteFileAsync(string filePath, Stream stream, CancellationToken cancellationToken = default);


        /// <summary>
        /// Creates a directory and any missing parent directories in the specified path.
        /// </summary>
        /// <param name="directoryPath">The directory path to create.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the operation succeeded.</returns>
        /// <exception cref="ArgumentException">Thrown when the provided path is not a directory path.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// The creation process starts from the deepest directory and moves upward to avoid
        /// unnecessary access checks on parent directories due to permission or performance constraints.
        /// Missing directories are created as needed.
        /// </remarks>
        Task<bool> CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);


        /// <summary>
        /// Moves a file from one location to another.
        /// </summary>
        /// <param name="sourceFilePath">The source file path.</param>
        /// <param name="destinationFilePath">The destination file path.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the operation succeeded.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the source file does not exist.</exception>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// Uses a single retry attempt to try to maintain atomic behavior, though full atomicity cannot be guaranteed.
        /// </remarks>
        Task<bool> MoveFileAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default);


        /// <summary>
        /// Moves a file using a previously fetched stream.
        /// </summary>
        /// <param name="stream">The stream containing file data.</param>
        /// <param name="sourceFilePath">The original file path.</param>
        /// <param name="destinationFilePath">The destination file path.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the operation succeeded.</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        /// <remarks>
        /// Writes the stream to the destination and deletes the source file.  
        /// Uses a single retry attempt to try to maintain atomic behavior, though full atomicity cannot be guaranteed.
        /// </remarks>
        Task<bool> MoveFileAsync(Stream stream, string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken = default);


        /// <summary>
        /// Deletes the file at the specified path if it exists.
        /// </summary>
        /// <param name="filePath">The full file path.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the operation succeeded.</returns>
        /// <exception cref="IOException">Thrown when the SMB operation fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the operation is cancelled.</exception>
        Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default);


        /// <summary>
        /// Tests whether the application can connect to the specified destination.
        /// </summary>
        /// <param name="directoryPath">The directory path to test.</param>
        /// <param name="cancellationToken">Token to cancel the operation.</param>
        /// <returns>True if the destination is reachable and accessible, otherwise false.</returns>
        /// <remarks>
        /// If the path is not a directory, an exception may be thrown on non-Windows or WSL environments.
        /// </remarks>
        Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default);
    }
}