using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsentSync.Data.Entities
{
    public enum ClientSource
    {
        PhisSearch = 0,
        AbleAccess = 1,
        ManualConsentForm = 2,
        DigitalConsentForm = 3,


        Other = 99
    }
    
}
