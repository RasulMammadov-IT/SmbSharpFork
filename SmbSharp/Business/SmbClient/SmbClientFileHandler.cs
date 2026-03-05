using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Logging;
using SmbSharp.Business.Interfaces;
using SmbSharp.Enums;
using SmbSharp.Infrastructure.Interfaces;

namespace SmbSharp.Business.SmbClient
{
    internal class SmbClientFileHandler : ISmbClientFileHandler
    {
        private readonly ILogger<SmbClientFileHandler> _logger;
        private readonly IProcessWrapper _processWrapper;
        private readonly bool _useKerberos;
        private readonly bool _useWsl;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string? _domain;

        private static readonly Regex SmbPathRegexInstance =
            new(@"^[/\\]{2}([^/\\]+)[/\\]([^/\\]+)(?:[/\\](.*))?$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegexInstance = new(@"\s+", RegexOptions.Compiled);

        // Matches smbclient ls output lines: 2 leading spaces, filename (may contain spaces),
        // 2+ spaces separator, attribute flags (capital letters), then size digit
        private static readonly Regex SmbLsLineRegexInstance =
            new(@"^\s{2}(.+?)\s{2,}([A-Z]+)\s+\d+", RegexOptions.Compiled);

        // Cache for smbclient availability check
        private static bool? _smbClientAvailable;
        private static readonly object _smbClientCheckLock = new();

        public bool IsSmbClientAvailable()
        {
            // Use cached result if available
            if (_smbClientAvailable.HasValue)
                return _smbClientAvailable.Value;

            lock (_smbClientCheckLock)
            {
                // Double-check after acquiring lock
                if (_smbClientAvailable.HasValue)
                    return _smbClientAvailable.Value;

                try
                {
                    ProcessResult result;
                    if (_useWsl)
                    {
                        // Check smbclient availability through WSL
                        var args = new List<string> { "smbclient", "--version" };
                        result = Task.Run(() => _processWrapper.ExecuteAsync("wsl", args)).Result;
                    }
                    else
                    {
                        result = Task.Run(() => _processWrapper.ExecuteAsync("smbclient", "--version")).Result;
                    }

                    _smbClientAvailable = result.ExitCode == 0;
                    return _smbClientAvailable.Value;
                }
                catch
                {
                    _smbClientAvailable = false;
                    return false;
                }
            }
        }

        public SmbClientFileHandler(ILogger<SmbClientFileHandler> logger, IProcessWrapper processWrapper,
            bool useKerberos, string? username = null, string? password = null,
            string? domain = null, bool useWsl = false)
        {
            _logger = logger;
            _processWrapper = processWrapper ?? throw new ArgumentNullException(nameof(processWrapper));
            _useKerberos = useKerberos;
            _useWsl = useWsl;
            if (!useKerberos && (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)))
            {
                _logger.LogError("Username and Password must be provided when not using Kerberos authentication.");
                throw new ArgumentException(
                    "Username and Password must be provided when not using Kerberos authentication.");
            }

            _username = username;
            _password = password;
            _domain = domain;
        }

        private async Task<List<FileEntry>> EnumerateInternalAsync(string server,
                                                                   string share,
                                                                   string? relativeCurrentPath,
                                                                   string directorySmbFullPath,
                                                                   CancellationToken cancellationToken)
        {
            List<FileEntry> entries = new();

            var command = string.IsNullOrEmpty(relativeCurrentPath)
                ? "ls"
                : $"cd \"{relativeCurrentPath}\"; ls";


            string output = null;

            output = await ExecuteSmbClientCommandAsync(
                server,
                share,
                command,
                directorySmbFullPath,
                false,
                cancellationToken);

            if(output == null)
            {
                return entries;
            }

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Parse smbclient ls output. Format per line (2 leading spaces):
            //   filename                            A      1234  Mon Jan  1 00:00:00 2024
            // Filenames may contain spaces, so we match via regex rather than whitespace split.
            foreach (var line in lines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (line.Contains("blocks of size") || line.Contains("blocks available"))
                    continue;

                var match = SmbLsLineRegexInstance.Match(line);
                if (!match.Success)
                    continue;

                var objectName = match.Groups[1].Value;
                var attributes = match.Groups[2].Value;

                // Skip . and ..
                if (objectName == "." || objectName == "..")
                    continue;

                var relativeFullPath = string.IsNullOrEmpty(relativeCurrentPath)
                    ? objectName
                    : $"{relativeCurrentPath}/{objectName}";

                var smbFullPath = string.IsNullOrEmpty(directorySmbFullPath)
                    ? objectName
                    : $"{directorySmbFullPath}/{objectName}";

                var entry = new FileEntry
                {
                    ObjectName = objectName,
                    RelativeFullPath = relativeFullPath,
                    SMBFullPath = smbFullPath.Replace('\\', '/'),
                    IsDirectory = attributes.Contains("D")
                };

                entries.Add(entry);
            }

            return entries;
        }

        private class FileEntry
        {
            public string? ObjectName { get; set; }
            public string? RelativeFullPath { get; set; }
            public string? SMBFullPath { get; set; }
            public bool IsDirectory { get; set; }
        }

        public async Task<bool> FileExistsAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var (fileName, directoryName) = GetFileAndDirectoryName(filePath);

                // Parse SMB path: //server/share/path or \\server\share\path
                var (server, share, relatedDirectoryPath) = ParseSmbPath(directoryName!);


                var fileEntries = await EnumerateInternalAsync(server, share, relatedDirectoryPath, directoryName, cancellationToken);

                foreach (var fileEntry in fileEntries)
                {
                    if (fileEntry.IsDirectory == false && string.Equals(fileEntry.ObjectName, fileName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (FileNotFoundException)
            {
                return false;
            }

            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if file exists. FilePath : {FilePath}", filePath);
                throw;
            }
        }

        public async Task<Stream> GetFileStreamAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            var (fileName, directoryName) = GetFileAndDirectoryName(filePath);

            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remoteFilePath) = ParseSmbPath(filePath);

            // Create a temporary local file
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");

            var command = $"get \"{remoteFilePath}\" \"{tempFilePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, filePath, false, cancellationToken);

            if (!File.Exists(tempFilePath))
            {
                throw new FileNotFoundException(
                    $"Failed to download file {fileName} from {filePath}");
            }

            // Return a FileStream with DeleteOnClose option to auto-cleanup temp file
            return new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.None, 4096,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        public async Task<bool> WriteFileAsync(string filePath, Stream stream, CancellationToken cancellationToken = default)
        {

            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, relativePath) = ParseSmbPath(filePath);

            var (fileName, relativeDirectoryName) = GetFileAndDirectoryName(relativePath);

            // Create a temporary local file to upload
            var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}_{fileName}");

            try
            {

                try
                {
                    var checkCommand = string.IsNullOrEmpty(relativeDirectoryName)
                        ? $"ls \"{fileName}\""
                        : $"cd \"{relativeDirectoryName}\"; ls \"{fileName}\"";

                    await ExecuteSmbClientCommandAsync(server, share, checkCommand, relativeDirectoryName, false, cancellationToken);

                    // If we get here, file exists
                    throw new FileAlreadyExistsException($"File already exists: {relativeDirectoryName}/{fileName}");
                }
                catch (FileNotFoundException)
                {
                    // Good - file doesn't exist, continue
                }


                await using var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write);

                await stream.CopyToAsync(fileStream, cancellationToken);
                await fileStream.FlushAsync();

                // Upload file using smbclient
                var command = $"put \"{tempFilePath}\" \"{relativePath}\"";
                await ExecuteSmbClientCommandAsync(server, share, command, relativePath, false, cancellationToken);

                return true;
            }
            finally
            {
                // Clean up temp file
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        public async Task<bool> DeleteFileAsync(string filePath,
            CancellationToken cancellationToken = default)
        {
            var (fileName, directoryName) = GetFileAndDirectoryName(filePath);

            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remoteFilePath) = ParseSmbPath(filePath);

            var command = $"del \"{remoteFilePath}\"";
            await ExecuteSmbClientCommandAsync(server, share, command, filePath, false, cancellationToken);

            return true;
        }

        public async Task<bool> CreateDirectoryAsync(string smbPath, CancellationToken cancellationToken = default)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, remotePath) = ParseSmbPath(smbPath);

            if (string.IsNullOrEmpty(remotePath))
            {
                throw new ArgumentException("Directory path cannot be empty", nameof(smbPath));
            }

            if (await DirectoryExistAsync(server, share, remotePath, smbPath, cancellationToken))
            {
                return true;
            }

            // Split into parts
            var parts = remotePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            // Find deepest existing parent
            int lastExistingIndex = -1;

            string[] allPathes = new string[parts.Length];

            for (int i = parts.Length; i > 0; i--)
            {
                var partial = string.Join("/", parts.Take(i));

                if (await DirectoryExistAsync(server, share, partial, smbPath, cancellationToken))
                {
                    lastExistingIndex = i - 1;
                    break;
                }

                allPathes[i - 1] = partial;
            }

            // If none exist, start from root of share
            int startIndex = lastExistingIndex + 1;

            // Create missing directories one by one
            for (int i = startIndex; i < parts.Length; i++)
            {
                var command = $"mkdir \"{allPathes[i]}\"";

                await ExecuteSmbClientCommandAsync(
                    server,
                    share,
                    command,
                    smbPath,
                    false,
                    cancellationToken);
            }

            return true;
        }

        private async Task<bool> DirectoryExistAsync(string server, string share, string relativeDirectory, string smbPath, CancellationToken cancellationToken = default)
        {

            if (string.IsNullOrEmpty(relativeDirectory))
            {
                throw new ArgumentException("Directory path cannot be empty", nameof(smbPath));
            }

            // Check if directory already exists to make this operation idempotent (consistent with Windows behavior)
            try
            {
                var checkCommand = string.IsNullOrEmpty(relativeDirectory)
                    ? "ls"
                    : $"cd \"{relativeDirectory}\"; ls";

                await ExecuteSmbClientCommandAsync(server, share, checkCommand, smbPath, false, cancellationToken);

                // If we reach here, the directory exists - return true (idempotent behavior)
                return true;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
        }

        private async Task<string> ExecuteSmbClientCommandAsync(string server, string share, string command,
            string contextPath, bool useMachineReadableFormat = false, CancellationToken cancellationToken = default)
        {
            string? credentialsFile = null;

            try
            {
                var argumentList = new List<string>();

                // When using WSL, prepend "smbclient" as the first argument (wsl will be the executable)
                if (_useWsl)
                {
                    argumentList.Add("smbclient");
                }

                // Add server/share
                argumentList.Add($"//{server}/{share}");

                if (_useKerberos)
                {
                    // Use Kerberos authentication (kinit ticket)
                    argumentList.Add("--use-kerberos=required");
                }
                else
                {
                    // Use username/password authentication via credentials file
                    var username = string.IsNullOrEmpty(_domain)
                        ? _username ?? string.Empty
                        : $"{_domain}\\{_username}";

                    // Create temporary credentials file
                    credentialsFile = Path.Combine(Path.GetTempPath(), $"smb_{Guid.NewGuid():N}.creds");
                    await File.WriteAllTextAsync(credentialsFile,
                        $"username={username}\npassword={_password}\n",
                        cancellationToken);

                    try
                    {
                        if (_useWsl)
                        {
                            var chmodArgs = new List<string> { "chmod", "600", ConvertToWslPath(credentialsFile) };
                            await _processWrapper.ExecuteAsync("wsl", chmodArgs, null, cancellationToken);
                        }
                        else
                        {
                            var chmodArgs = new List<string> { "600", credentialsFile };
                            await _processWrapper.ExecuteAsync("chmod", chmodArgs, null, cancellationToken);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning(e, "Failed to set permissions on SMB credentials file.");
                    }

                    // Use credentials file (convert path for WSL if needed)
                    argumentList.Add("-A");
                    argumentList.Add(_useWsl ? ConvertToWslPath(credentialsFile) : credentialsFile);
                }

                if (useMachineReadableFormat)
                {
                    // Use machine-readable output format for easier parsing (no color codes, consistent spacing)
                    argumentList.Add("-g");
                }

                // Add command (convert any Windows paths in the command for WSL)
                argumentList.Add("-c");
                argumentList.Add(_useWsl ? ConvertWindowsPathsInCommand(command) : command);

                var executable = _useWsl ? "wsl" : "smbclient";
                var result = await _processWrapper.ExecuteAsync(executable, argumentList, null, cancellationToken);

                if (result.ExitCode == 0)
                {
                    return result.StandardOutput;
                }

                // Try to differentiate error types based on smbclient error messages
                // Check both stdout and stderr as smbclient can output errors to either
                var errorOutput = $"{result.StandardOutput} {result.StandardError}";
                var errorLower = errorOutput.ToLowerInvariant();

                if (errorLower.Contains("does not exist") ||
                    errorLower.Contains("not found") ||
                    errorLower.Contains("nt_status_object_name_not_found") ||
                    errorLower.Contains("nt_status_object_path_not_found") ||
                    errorLower.Contains("nt_status_no_such_file"))
                {
                    throw new FileNotFoundException(
                        $"The specified path was not found on {contextPath}", contextPath);
                }

                if (errorLower.Contains("access denied") ||
                    errorLower.Contains("permission denied") ||
                    errorLower.Contains("nt_status_access_denied") ||
                    errorLower.Contains("logon failure"))
                {
                    throw new UnauthorizedAccessException(
                        $"Access denied to {contextPath}: {result.StandardError}");
                }

                if (errorLower.Contains("bad network path") ||
                    errorLower.Contains("network name not found") ||
                    errorLower.Contains("nt_status_bad_network_name"))
                {
                    throw new DirectoryNotFoundException(
                        $"The network path was not found: {contextPath}");
                }

                // Generic error for everything else
                throw new IOException(
                    $"Failed to execute smbclient command on {contextPath}: {result.StandardError}");
            }
            finally
            {
                // Clean up credentials file
                if (credentialsFile != null)
                {
                    try
                    {
                        File.Delete(credentialsFile);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }

        private static (string server, string share, string path) ParseSmbPath(string smbPath)
        {
            // Parse SMB path: //server/share/path or \\server\share\path
            var match = SmbPathRegexInstance.Match(smbPath);
            if (!match.Success)
            {
                throw new ArgumentException($"Invalid SMB path format: {smbPath}");
            }

            var server = match.Groups[1].Value;
            var share = match.Groups[2].Value;
            var path = match.Groups[3].Success ? match.Groups[3].Value.Replace('\\', '/') : "";

            return (server, share, path);
        }

        public async Task<bool> CanConnectAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            try
            {
                // Parse SMB path: //server/share/path or \\server\share\path
                var (server, share, path) = ParseSmbPath(directoryPath);

                // Try to list files to test connection - if path is specified, check that specific directory
                var command = string.IsNullOrEmpty(path) ? "ls" : $"cd \"{path}\"; ls";
                await ExecuteSmbClientCommandAsync(server, share, command, directoryPath, false, cancellationToken);

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Converts a Windows absolute path to a WSL path.
        /// Example: C:\Users\user\file.txt → /mnt/c/Users/user/file.txt
        /// </summary>
        private static string ConvertToWslPath(string windowsPath)
        {
            if (string.IsNullOrEmpty(windowsPath) || windowsPath.Length < 3)
                return windowsPath;

            // Match drive letter pattern like C:\ or C:/
            if (char.IsLetter(windowsPath[0]) && windowsPath[1] == ':' &&
                (windowsPath[2] == '\\' || windowsPath[2] == '/'))
            {
                var drive = char.ToLowerInvariant(windowsPath[0]);
                var rest = windowsPath.Substring(3).Replace('\\', '/');
                return $"/mnt/{drive}/{rest}";
            }

            return windowsPath;
        }

        /// <summary>
        /// Converts any Windows absolute paths found within a command string to WSL paths.
        /// Used for smbclient -c commands that contain local file paths (get/put operations).
        /// </summary>
        private static string ConvertWindowsPathsInCommand(string command)
        {
            // Match Windows absolute paths like C:\path or D:/path within the command
            return Regex.Replace(command, @"([A-Za-z]):([\\/])([^\s""]*)", match =>
            {
                var drive = char.ToLowerInvariant(match.Groups[1].Value[0]);
                var rest = match.Groups[3].Value.Replace('\\', '/');
                return $"/mnt/{drive}/{rest}";
            });
        }

        private (string fileName, string directoryName) GetFileAndDirectoryName(string filePath)
        {
            filePath = filePath.Replace('\\', '/');

            int lastSlashIndex = filePath.LastIndexOf('/');

            if (lastSlashIndex < 0)
            {
                return ("", filePath);
            }

            string directoryName = filePath.Substring(0, lastSlashIndex);
            string fileName = filePath.Substring(lastSlashIndex + 1);

            return (fileName, directoryName);
        }

        public async IAsyncEnumerable<string> EnumerateFilesAsync(string smbPath, bool searchAllDirectories = true, CancellationToken cancellationToken = default)
        {
            var files = new List<string>();

            // Parse SMB path: //server/share/path or \\server\share\path
            var (server, share, path) = ParseSmbPath(smbPath);

            if (searchAllDirectories is false)
            {
                var entriesInScope = await EnumerateInternalAsync(server, share, path, smbPath!, cancellationToken);

                foreach (var entry in entriesInScope)
                {
                    if (entry.IsDirectory == false)
                    {
                        yield return entry.SMBFullPath!;
                    }
                }

                yield break;
            }

            Queue<FileEntry> directories = new();

            directories.Enqueue(new FileEntry()
            {
                RelativeFullPath = path,
                SMBFullPath = smbPath,
                IsDirectory = true
            });

            while (directories.Count > 0)
            {
                var currentFileEntry = directories.Dequeue();

                var entriesInScope = await EnumerateInternalAsync(server, share, currentFileEntry.RelativeFullPath, currentFileEntry.SMBFullPath!, cancellationToken);

                foreach (var entry in entriesInScope)
                {
                    if (entry.IsDirectory)
                    {
                        directories.Enqueue(entry);
                    }
                    else
                    {
                        yield return entry.SMBFullPath!;
                    }
                }
            }

            yield break;

        }
    }
}