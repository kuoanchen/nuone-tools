using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace nuone_tools
{
    public sealed class AccountSettingsState
    {
        public string ApiBaseUrl { get; set; } = "https://api.nuone.cl";

        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public string UserDisplayName { get; set; } = string.Empty;

        public string ServiceAccountsSummary { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        public string ServiceAccountsJson { get; set; } = string.Empty;

        public string LastLoginText { get; set; } = "尚未登入";

        public string LastStatusText { get; set; } = "尚未登入";

        public static AccountSettingsState CreateDefault()
        {
            return new AccountSettingsState();
        }
    }

    public sealed class AccountLoginResult
    {
        public string Token { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string ServiceAccountsSummary { get; set; } = string.Empty;

        public string PayloadJson { get; set; } = string.Empty;

        public string ServiceAccountsJson { get; set; } = string.Empty;
    }
}
