using System;
using System.Collections.Generic;
using System.Text;

namespace MinooWebReports.Models
{
    public class UserModel 
    {
        public decimal UsersKeyId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public decimal? VahedeTejariSn { get; set; }
        public string UserPassword { get; set; }
        public string ExpireDate { get; set; }
    }
}
