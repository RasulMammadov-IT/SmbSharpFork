using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmbSharp.Business.Interfaces;
using SmbSharp.Business.SmbClient;
using SmbSharp.Enums;
using SmbSharp.Infrastructure;

namespace SmbSharp.Business
{
    /// <summary>
    /// Implementation of IFileHandler that provides SMB/CIFS file operations across different platforms.
    /// Uses native UNC paths on Windows (or smbclient via WSL if opted in) and smbclient on Linux/macOS.
    /// </summary>
    public class FileHandler : IFileHandler
    {
        private readonly ILogger<FileHandler> _logger;
        private readonly ISmbClientFileHandler _smbClientFileHandler;
        private readonly bool _useSmbClient;

        /// <summary>
        /// Creates a new FileHandler using Kerberos authentication.
        /// On Linux/macOS, requires a valid Kerberos ticket (kinit) and smbclient installed.
        /// On Windows, uses native UNC paths by default. Set useWsl to true to use smbclient via WSL instead.
        /// </summary>
        /// <param name="loggerFactory">Optional logger factory for debug output. Pass null to disable logging.</param>
        /// <param name="useWsl">When true on Windows, uses smbclient via WSL instead of native UNC paths. Ignored on Linux/macOS.</param>
        /// <returns>A new FileHandler instance</returns>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS (or via WSL when useWsl is true)</exception>
        public static FileHandler CreateWithKerberos(ILoggerFactory? loggerFactory = null, bool useWsl = false)
        {
            loggerFactory ??= new NullLoggerFactory();
            var processWrapper = new ProcessWrapper(loggerFactory.CreateLogger<ProcessWrapper>());
            var smbClientHandler = new SmbClientFileHandler(
                loggerFactory.CreateLogger<SmbClientFileHandler>(),
                processWrapper,
                useKerberos: true,
                useWsl: useWsl);

            return new FileHandler(loggerFactory.CreateLogger<FileHandler>(), smbClientHandler, useWsl);
        }

        /// <summary>
        /// Creates a new FileHandler using username/password authentication.
        /// On Linux/macOS, requires smbclient installed.
        /// On Windows, uses native UNC paths by default. Set useWsl to true to use smbclient via WSL instead.
        /// </summary>
        /// <param name="username">Username for authentication</param>
        /// <param name="password">Password for authentication</param>
        /// <param name="domain">Optional domain for authentication</param>
        /// <param name="loggerFactory">Optional logger factory for debug output. Pass null to disable logging.</param>
        /// <param name="useWsl">When true on Windows, uses smbclient via WSL instead of native UNC paths. Ignored on Linux/macOS.</param>
        /// <returns>A new FileHandler instance</returns>
        /// <exception cref="ArgumentException">Thrown when username or password is null or empty</exception>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS (or via WSL when useWsl is true)</exception>
        public static FileHandler CreateWithCredentials(string username, string password, string? domain = null,
            ILoggerFactory? loggerFactory = null, bool useWsl = false)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new ArgumentException("Username cannot be null or empty", nameof(username));
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password cannot be null or empty", nameof(password));

            loggerFactory ??= new NullLoggerFactory();
            var processWrapper = new ProcessWrapper(loggerFactory.CreateLogger<ProcessWrapper>());
            var smbClientHandler = new SmbClientFileHandler(
                loggerFactory.CreateLogger<SmbClientFileHandler>(),
                processWrapper,
                useKerberos: false,
                username,
                password,
                domain,
                useWsl: useWsl);

            return new FileHandler(loggerFactory.CreateLogger<FileHandler>(), smbClientHandler, useWsl);
        }

        /// <summary>
        /// Initializes a new instance of FileHandler.
        /// This constructor is used by dependency injection.
        /// On Windows, uses native UNC paths. On Linux/macOS, uses smbclient.
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS</exception>
        public FileHandler(ILogger<FileHandler> logger, ISmbClientFileHandler smbClientFileHandler)
            : this(logger, smbClientFileHandler, useWsl: false)
        {
        }

        /// <summary>
        /// Initializes a new instance of FileHandler with optional WSL support.
        /// On Windows with useWsl=false, uses native UNC paths.
        /// On Windows with useWsl=true, uses smbclient via WSL.
        /// On Linux/macOS, uses smbclient directly (useWsl is ignored).
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <param name="smbClientFileHandler">The smbclient file handler</param>
        /// <param name="useWsl">When true on Windows, uses smbclient via WSL instead of native UNC paths</param>
        /// <exception cref="PlatformNotSupportedException">Thrown when running on unsupported platform</exception>
        /// <exception cref="InvalidOperationException">Thrown when smbclient is not available on Linux/macOS (or via WSL when useWsl is true)</exception>
        public FileHandler(ILogger<FileHandler> logger, ISmbClientFileHandler smbClientFileHandler, bool useWsl)
        {
            _logger = logger;
            _smbClientFileHandler = smbClientFileHandler;

            // On Linux/macOS, always use smbclient. On Windows, only if useWsl is opted in.
            _useSmbClient = !RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || useWsl;

            ValidatePlatformAndDependencies();
        }

        private void ValidatePlatformAndDependencies()
        {
            // Check if running on supported platform (Windows, Linux, or macOS)
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
                !RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _logger.LogError("Unsupported platform: {Platform}", RuntimeInformation.OSDescription);
                throw new PlatformNotSupportedException(
                    "SmbSharp only supports Windows, Linux, and macOS platforms. " +
                    $"Current platform: {RuntimeInformation.OSDescription}");
            }

            if (_useSmbClient)
            {
                // Verify smbclient is available
                if (!_smbClientFileHandler.IsSmbClientAvailable())
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        _logger.LogError("smbclient is not available through WSL.");
                        throw new InvalidOperationException(
                            "smbclient is not available through WSL. " +
                            "Ensure WSL is installed and smbclient is available inside your WSL distribution: " +
                            "wsl apt-get install smbclient");
                    }

                    _logger.LogError("smbclient is not installed or not available in PATH.");
                    throw new InvalidOperationException(
                        "smbclient is not installed or not available in PATH. " +
                        "Install it using: apt-get install smbclient (Debian/Ubuntu), " +
                        "yum install samba-client (RHEL/CentOS), or brew install samba (macOS)");
                }
            }
        }

        /// <inheritdoc/>
        public async Task<bool> FileExistsAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.FileExistsAsync(filePath, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return File.Exists(filePath);
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<Stream> ReadFileAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.GetFileStreamAsync(filePath, cancellationToken);
            }

            // Use direct IO - safely combine paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException(
                        $"The file {filePath} could not be found or I don't have access to it");
                }

                return (Stream)new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    FileOptions.Asynchronous);
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, string content,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return await WriteFileAsync(filePath, stream, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> WriteFileAsync(string filePath, Stream stream,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Reset stream position if seekable
            if (stream.CanSeek && stream.Position != 0)
            {
                stream.Position = 0;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (!_useSmbClient)
            {
                // Use direct IO operations for UNC paths
                return await Task.Run(async () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        4096, FileOptions.Asynchronous);
                    await stream.CopyToAsync(fileStream, cancellationToken);
                    return true;
                }, cancellationToken);
            }

            return await _smbClientFileHandler.WriteFileAsync(filePath, stream,
                cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> CreateDirectoryAsync(string directoryPath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await _smbClientFileHandler.CreateDirectoryAsync(directoryPath, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                return true;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> MoveFileAsync(string sourceFilePath, string destinationFilePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path cannot be null or empty", nameof(sourceFilePath));
            if (string.IsNullOrWhiteSpace(destinationFilePath))
                throw new ArgumentException("Destination file directory cannot be null or empty",
                    nameof(destinationFilePath));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // For smbclient, we need to download and re-upload since there's no native move command.
                // This operation is made atomic with retry logic and rollback on failure.

                bool destinationWritten = false;
                try
                {
                    // Step 1: Read source file into memory
                    await using var stream =
                        await _smbClientFileHandler.GetFileStreamAsync(sourceFilePath, cancellationToken);

                    // Step 2: Write to destination location
                    await _smbClientFileHandler.WriteFileAsync(destinationFilePath, stream, cancellationToken);
                    destinationWritten = true;

                    // Step 3: Delete source file to complete the move
                    await _smbClientFileHandler.DeleteFileAsync(sourceFilePath, cancellationToken);

                    return true;
                }
                catch
                {
                    // Atomic operation: If destination was written but source deletion failed,
                    // retry once to handle transient issues before rolling back
                    if (destinationWritten)
                    {
                        try
                        {
                            _logger.LogWarning(
                                "Failed to delete source file {SourceFilePath} after copying, retrying once...",
                                sourceFilePath);

                            // Brief delay to handle transient network or file lock issues
                            await Task.Delay(100, cancellationToken);

                            // Retry: Attempt to delete source file one more time
                            await _smbClientFileHandler.DeleteFileAsync(sourceFilePath, cancellationToken);

                            // Success: Retry completed the move operation
                            return true;
                        }
                        catch
                        {
                            // Rollback: Both attempts failed, delete destination to maintain atomicity
                            // This ensures the file exists in only the original location
                            _logger.LogError(
                                "Retry failed to delete source file {SourceFilePath}, rolling back destination",
                                sourceFilePath);

                            try
                            {
                                await _smbClientFileHandler.DeleteFileAsync(destinationFilePath, cancellationToken);
                            }
                            catch
                            {
                                // Cleanup failed - log but don't mask the original exception
                                _logger.LogError(
                                    "Failed to cleanup destination file {DestinationFilePath} after move operation failed",
                                    destinationFilePath);
                            }
                        }
                    }

                    throw;
                }
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                File.Move(sourceFilePath, destinationFilePath);
                return true;
            }, cancellationToken);
        }


        /// <inheritdoc/>
        public async Task<bool> MoveFileAsync(Stream stream, string sourceFilePath, string destinationFilePath,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Source file path cannot be null or empty", nameof(sourceFilePath));
            if (string.IsNullOrWhiteSpace(destinationFilePath))
                throw new ArgumentException("Destination file directory cannot be null or empty",
                    nameof(destinationFilePath));

            if (_useSmbClient)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // For smbclient, we need to download and re-upload since there's no native move command.
                // This operation is made atomic with retry logic and rollback on failure.

                bool destinationWritten = false;
                try
                {
                    // Step 1: Write to destination location
                    await _smbClientFileHandler.WriteFileAsync(destinationFilePath, stream, cancellationToken);
                    destinationWritten = true;

                    // Step 2: Delete source file to complete the move
                    await _smbClientFileHandler.DeleteFileAsync(sourceFilePath, cancellationToken);

                    return true;
                }
                catch
                {
                    // Atomic operation: If destination was written but source deletion failed,
                    // retry once to handle transient issues before rolling back
                    if (destinationWritten)
                    {
                        try
                        {
                            _logger.LogWarning(
                                "Failed to delete source file {SourceFilePath} after copying, retrying once...",
                                sourceFilePath);

                            // Brief delay to handle transient network or file lock issues
                            await Task.Delay(100, cancellationToken);

                            // Retry: Attempt to delete source file one more time
                            await _smbClientFileHandler.DeleteFileAsync(sourceFilePath, cancellationToken);

                            // Success: Retry completed the move operation
                            return true;
                        }
                        catch
                        {
                            // Rollback: Both attempts failed, delete destination to maintain atomicity
                            // This ensures the file exists in only the original location
                            _logger.LogError(
                                "Retry failed to delete source file {SourceFilePath}, rolling back destination",
                                sourceFilePath);

                            try
                            {
                                await _smbClientFileHandler.DeleteFileAsync(destinationFilePath, cancellationToken);
                            }
                            catch
                            {
                                // Cleanup failed - log but don't mask the original exception
                                _logger.LogError(
                                    "Failed to cleanup destination file {DestinationFilePath} after move operation failed",
                                    destinationFilePath);
                            }
                        }
                    }

                    throw;
                }
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    File.Move(sourceFilePath, destinationFilePath);
                }
                catch (IOException ex) when (
                    ex.HResult == unchecked((int)0x80070050) ||  // ERROR_FILE_EXISTS
                    ex.HResult == unchecked((int)0x800700B7))    // ERROR_ALREADY_EXISTS
                {
                    throw new FileAlreadyExistsException($"File already exists: {destinationFilePath}");
                }

                return true;
            }, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

            cancellationToken.ThrowIfCancellationRequested();

            if (!_useSmbClient)
            {
                // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
                return await Task.Run(() =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    return true;
                }, cancellationToken);
            }

            return await _smbClientFileHandler.DeleteFileAsync(filePath, cancellationToken);
        }

        /// <inheritdoc/>
        public async Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
                return false;

            if (_useSmbClient)
            {
                return await _smbClientFileHandler.CanConnectAsync(directoryPath, cancellationToken);
            }

            // Use direct IO operations for UNC paths - wrap in Task.Run to avoid blocking
            return await Task.Run(() => Directory.Exists(directoryPath), cancellationToken);
        }

        public async IAsyncEnumerable<string> EnumerateFilesAsync(string directory, bool searchAllDirectories = true, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("Directory path cannot be null or empty", nameof(directory));

            // SMB branch
            if (_useSmbClient)
            {
                await foreach (var file in _smbClientFileHandler.EnumerateFilesAsync(directory, searchAllDirectories, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return file;
                }

                yield break; // SMB branch handled
            }

            // Local filesystem branch
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    $"The directory {directory} could not be found or I don't have access to it");
            }

            var searchOption = searchAllDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var filePath in Directory.EnumerateFiles(directory, "*", searchOption))
            {
                yield return filePath;
            }
        }
    }
}