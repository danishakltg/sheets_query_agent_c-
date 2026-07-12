using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;

namespace SheetsQueryAgent
{
    public static class DotEnv
    {
        public static void Load(string? searchDir = null)
        {
            string currentDir = searchDir ?? Directory.GetCurrentDirectory();
            string envPath = Path.Combine(currentDir, ".env");
            
            // If not found in current dir, search up to parent directory
            if (!File.Exists(envPath))
            {
                var parent = Directory.GetParent(currentDir);
                if (parent != null)
                {
                    envPath = Path.Combine(parent.FullName, ".env");
                }
            }

            if (!File.Exists(envPath))
            {
                Console.WriteLine($"[DotEnv Warning] .env file not found. Using existing environment variables.");
                return;
            }

            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var val = parts[1].Trim().Trim('"').Trim('\'');
                Environment.SetEnvironmentVariable(key, val);
            }
        }
    }

    public class GoogleSheetsDataLayer
    {
        public bool IsMock { get; private set; }
        private string SpreadsheetId { get; set; }
        private string CredentialsPath { get; set; }
        private SheetsService? service;

        // Rich Mock Database representing sample spreadsheet data
        private static readonly Dictionary<string, List<Dictionary<string, string>>> MOCK_SHEETS = new()
        {
            {
                "Sheet1", new List<Dictionary<string, string>>
                {
                    new() { { "ID", "1001" }, { "Name", "Wireless Mouse" }, { "Category", "Electronics" }, { "Price", "29.99" }, { "Stock", "150" }, { "Status", "In Stock" } },
                    new() { { "ID", "1002" }, { "Name", "Mechanical Keyboard" }, { "Category", "Electronics" }, { "Price", "89.99" }, { "Stock", "45" }, { "Status", "In Stock" } },
                    new() { { "ID", "1003" }, { "Name", "Ergonomic Chair" }, { "Category", "Furniture" }, { "Price", "199.99" }, { "Stock", "12" }, { "Status", "Low Stock" } },
                    new() { { "ID", "1004" }, { "Name", "LED Desk Lamp" }, { "Category", "Electronics" }, { "Price", "34.50" }, { "Stock", "80" }, { "Status", "In Stock" } },
                    new() { { "ID", "1005" }, { "Name", "Notebook Journal" }, { "Category", "Stationery" }, { "Price", "12.99" }, { "Stock", "200" }, { "Status", "In Stock" } },
                    new() { { "ID", "1006" }, { "Name", "Gel Pen Set" }, { "Category", "Stationery" }, { "Price", "8.50" }, { "Stock", "0" }, { "Status", "Out of Stock" } },
                    new() { { "ID", "1007" }, { "Name", "Standing Desk" }, { "Category", "Furniture" }, { "Price", "350.00" }, { "Stock", "5" }, { "Status", "Low Stock" } }
                }
            },
            {
                "Sales", new List<Dictionary<string, string>>
                {
                    new() { { "Order ID", "SO-001" }, { "Date", "2026-07-01" }, { "Customer ID", "C-201" }, { "Product ID", "1001" }, { "Quantity", "2" }, { "Total", "59.98" } },
                    new() { { "Order ID", "SO-002" }, { "Date", "2026-07-02" }, { "Customer ID", "C-202" }, { "Product ID", "1003" }, { "Quantity", "1" }, { "Total", "199.99" } },
                    new() { { "Order ID", "SO-003" }, { "Date", "2026-07-05" }, { "Customer ID", "C-203" }, { "Product ID", "1005" }, { "Quantity", "5" }, { "Total", "64.95" } },
                    new() { { "Order ID", "SO-004" }, { "Date", "2026-07-07" }, { "Customer ID", "C-201" }, { "Product ID", "1002" }, { "Quantity", "1" }, { "Total", "89.99" } },
                    new() { { "Order ID", "SO-005" }, { "Date", "2026-07-08" }, { "Customer ID", "C-204" }, { "Product ID", "1006" }, { "Quantity", "3" }, { "Total", "25.50" } }
                }
            }
        };

        public GoogleSheetsDataLayer()
        {
            DotEnv.Load();
            
            var mockModeEnv = Environment.GetEnvironmentVariable("MOCK_MODE");
            IsMock = string.IsNullOrEmpty(mockModeEnv) || mockModeEnv.ToLower() == "true";
            
            SpreadsheetId = Environment.GetEnvironmentVariable("SPREADSHEET_ID") ?? "1KsKmgmtiELtMvVFHkE9QLvt98Bb7LqJv_erA8OjTJJo";
            CredentialsPath = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS") ?? "credentials.json";

            if (!IsMock)
            {
                InitGoogleSheets();
            }
        }

        private void InitGoogleSheets()
        {
            try
            {
                // Resolve path relative to current dir or parent if needed
                string resolvedCredsPath = CredentialsPath;
                if (!File.Exists(resolvedCredsPath))
                {
                    var parent = Directory.GetParent(Directory.GetCurrentDirectory());
                    if (parent != null)
                    {
                        resolvedCredsPath = Path.Combine(parent.FullName, CredentialsPath);
                    }
                }

                if (!File.Exists(resolvedCredsPath))
                {
                    Console.WriteLine($"[Data Layer Warning] Credentials file not found at {CredentialsPath}. Falling back to MOCK mode.");
                    IsMock = true;
                    return;
                }

                string[] scopes = { SheetsService.Scope.Spreadsheets, SheetsService.Scope.Drive };
                GoogleCredential credential;
                using (var stream = new FileStream(resolvedCredsPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream).CreateScoped(scopes);
                }

                service = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "SheetsQueryAgent"
                });

                // Validate spreadsheet connection by getting properties
                var spreadsheet = service.Spreadsheets.Get(SpreadsheetId).Execute();
                Console.WriteLine($"[Data Layer] Successfully connected to live Google Sheet: '{spreadsheet.Properties.Title}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Data Layer Error] Failed to initialize Google Sheets client: {ex.Message}. Falling back to MOCK mode.");
                IsMock = true;
            }
        }

        public List<string> GetSheetNames()
        {
            if (IsMock)
            {
                Console.WriteLine("[Data Layer] [MOCK] Retrieving sheet names.");
                return MOCK_SHEETS.Keys.ToList();
            }

            try
            {
                var spreadsheet = service!.Spreadsheets.Get(SpreadsheetId).Execute();
                return spreadsheet.Sheets.Select(s => s.Properties.Title).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Data Layer Error] Failed to retrieve sheet names: {ex.Message}");
                return new List<string>();
            }
        }

        public List<string> GetSheetSchema(string sheetName)
        {
            if (IsMock)
            {
                Console.WriteLine($"[Data Layer] [MOCK] Retrieving headers for sheet '{sheetName}'.");
                if (MOCK_SHEETS.TryGetValue(sheetName, out var records) && records.Count > 0)
                {
                    return records[0].Keys.ToList();
                }
                return new List<string>();
            }

            try
            {
                // Fetch first row (A1:Z1) to get headers
                var request = service!.Spreadsheets.Values.Get(SpreadsheetId, $"{sheetName}!1:1");
                var response = request.Execute();
                var values = response.Values;
                if (values != null && values.Count > 0)
                {
                    return values[0].Select(v => v?.ToString() ?? "").ToList();
                }
                return new List<string>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Data Layer Error] Failed to retrieve schema for sheet '{sheetName}': {ex.Message}");
                return new List<string>();
            }
        }

        public List<Dictionary<string, string>> GetSheetData(string sheetName)
        {
            if (IsMock)
            {
                Console.WriteLine($"[Data Layer] [MOCK] Fetching all rows from sheet '{sheetName}'.");
                if (MOCK_SHEETS.TryGetValue(sheetName, out var records))
                {
                    // Return a copy to prevent external mutation issues
                    return records.Select(r => new Dictionary<string, string>(r)).ToList();
                }
                return new List<Dictionary<string, string>>();
            }

            try
            {
                // Fetch the full sheet grid
                var request = service!.Spreadsheets.Values.Get(SpreadsheetId, $"{sheetName}!A:Z");
                var response = request.Execute();
                var values = response.Values;
                var list = new List<Dictionary<string, string>>();

                if (values == null || values.Count == 0)
                {
                    return list;
                }

                // First row contains the column headers
                var headers = values[0].Select(v => v?.ToString() ?? "").ToList();

                // Process rest of the rows
                for (int i = 1; i < values.Count; i++)
                {
                    var row = values[i];
                    var record = new Dictionary<string, string>();
                    for (int j = 0; j < headers.Count; j++)
                    {
                        string header = headers[j];
                        string val = j < row.Count ? (row[j]?.ToString() ?? "") : "";
                        record[header] = val;
                    }
                    list.Add(record);
                }

                return list;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Data Layer Error] Failed to retrieve records from sheet '{sheetName}': {ex.Message}");
                return new List<Dictionary<string, string>>();
            }
        }

        public List<Dictionary<string, string>> SearchSheetRows(string sheetName, string filterCol, string searchVal)
        {
            if (IsMock)
            {
                Console.WriteLine($"[Data Layer] [MOCK] Searching sheet '{sheetName}' where '{filterCol}' is '{searchVal}'.");
            }
            
            var records = GetSheetData(sheetName);
            var filtered = new List<Dictionary<string, string>>();
            
            foreach (var r in records)
            {
                if (r.TryGetValue(filterCol, out var val))
                {
                    if (val.IndexOf(searchVal, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        filtered.Add(r);
                    }
                }
            }

            return filtered;
        }

        public bool AppendSheetRow(string sheetName, List<object> rowData)
        {
            if (IsMock)
            {
                Console.WriteLine($"[Data Layer] [MOCK] Appending row to sheet '{sheetName}': [{string.Join(", ", rowData)}]");
                var headers = GetSheetSchema(sheetName);
                var newRecord = new Dictionary<string, string>();
                
                for (int i = 0; i < rowData.Count; i++)
                {
                    if (i < headers.Count)
                    {
                        newRecord[headers[i]] = rowData[i]?.ToString() ?? "";
                    }
                }

                if (!MOCK_SHEETS.ContainsKey(sheetName))
                {
                    MOCK_SHEETS[sheetName] = new List<Dictionary<string, string>>();
                }
                MOCK_SHEETS[sheetName].Add(newRecord);
                return true;
            }

            try
            {
                var valueRange = new ValueRange
                {
                    Values = new List<IList<object>> { rowData }
                };

                var appendRequest = service!.Spreadsheets.Values.Append(valueRange, SpreadsheetId, $"{sheetName}!A1");
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.AppendRequest.ValueInputOptionEnum.USERENTERED;
                appendRequest.Execute();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Data Layer Error] Failed to append row to sheet '{sheetName}': {ex.Message}");
                return false;
            }
        }
    }

    // Singleton / Helper instance
    public static class DataLayer
    {
        private static GoogleSheetsDataLayer? _dataLayer;

        public static GoogleSheetsDataLayer GetInstance()
        {
            return _dataLayer ??= new GoogleSheetsDataLayer();
        }

        public static List<string> QuerySheetTabs()
        {
            return GetInstance().GetSheetNames();
        }

        public static List<string> QuerySheetHeaders(string sheetName)
        {
            return GetInstance().GetSheetSchema(sheetName);
        }

        public static List<Dictionary<string, string>> QuerySheetAllData(string sheetName)
        {
            return GetInstance().GetSheetData(sheetName);
        }

        public static List<Dictionary<string, string>> QuerySheetFilter(string sheetName, string columnName, string searchValue)
        {
            return GetInstance().SearchSheetRows(sheetName, columnName, searchValue);
        }

        public static string AddRowToSheet(string sheetName, List<object> values)
        {
            bool success = GetInstance().AppendSheetRow(sheetName, values);
            if (success)
            {
                return $"Successfully added row to sheet '{sheetName}': [{string.Join(", ", values)}]";
            }
            else
            {
                return $"Failed to add row to sheet '{sheetName}'";
            }
        }
    }
}
