using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSync.Data.Entities
{
    public class PhisClientCacheEntity
    {
        public int Id { get; set; } // Optional surrogate primary key

        // Generated via: $"{LastName}{FirstName}_{DateOfBirth}".ToUpperInvariant()
        public string CacheKey { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty; // PHIS System ID (e.g. 1512481)
        public string FullName { get; set; } = string.Empty;
        public string DateOfBirth { get; set; } = string.Empty;
        public string? Email { get; set; }
        public ClientSource Source { get; set; } = ClientSource.PhisSearch;
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
    }
}
