using System;

namespace UniGetUI.PhisonService
{
    /// <summary>
    /// Error codes for service operations
    /// </summary>
    public enum ServiceErrorCode
    {
        // General codes
        /// <summary>
        /// Operation completed successfully
        /// </summary>
        Success = 0,

        /// <summary>
        /// Service is in initial/idle state
        /// </summary>
        Init = 1,

        /// <summary>
        /// Operation failed (details in error message)
        /// </summary>
        Fail = -1,

        // Status codes
        /// <summary>
        /// aiDAPTIV Service is starting
        /// </summary>
        AiDAPTIVStarting = 10,

        // Status codes
        /// <summary>
        /// aiDAPTIV Service is polling
        /// </summary>
        AiDAPTIVPolling = 11,

        // Status codes
        /// <summary>
        /// App Service is starting
        /// </summary>
        AppStarting = 12,

        /// <summary>
        /// aiDAPTIV Service is stopping
        /// </summary>
        AiDAPTIVStopping = 20,

        /// <summary>
        /// aiDAPTIV Service is killing
        /// </summary>
        AiDAPTIVKilling = 21,

        /// <summary>
        /// App Service is stopping
        /// </summary>
        AppStopping = 22
    }
}

