namespace MovaCore.Services
{
    /// <summary>Where MovaCore is registered to start with Windows.</summary>
    public interface IStartupRegistration
    {
        /// <summary>The registered executable path, or null if MovaCore is not registered.</summary>
        string? GetRegisteredPath();

        void Register(string executablePath);

        void Unregister();
    }
}
