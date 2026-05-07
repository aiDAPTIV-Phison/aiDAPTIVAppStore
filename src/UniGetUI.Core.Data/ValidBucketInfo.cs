namespace UniGetUI.Core.Data
{
    /// <summary>
    /// Represents a valid bucket with name and URL.
    /// Used for configuring allowed Scoop buckets via ValidBucketList.json.
    /// </summary>
    public record ValidBucketInfo
    {
        /// <summary>
        /// The display name of the bucket (e.g., "aiDAPTIV-bucket")
        /// </summary>
        public string Name { get; init; } = "";

        /// <summary>
        /// The repository URL of the bucket (e.g., "https://github.com/aiDAPTIV-Phison/aiDAPTIV-bucket")
        /// </summary>
        public string Url { get; init; } = "";

        /// <summary>
        /// Returns the normalized URL (without trailing slash) for comparison
        /// </summary>
        public string NormalizedUrl => Url.TrimEnd('/');
    }
}
