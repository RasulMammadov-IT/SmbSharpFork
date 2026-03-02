
public class FileAlreadyExistsException : IOException
{
    public string? FilePath { get; }

    public FileAlreadyExistsException()
    {
    }

    public FileAlreadyExistsException(string message)
        : base(message)
    {
    }

    public FileAlreadyExistsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FileAlreadyExistsException(string message, string filePath)
        : base(message)
    {
        FilePath = filePath;
    }
}