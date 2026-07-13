using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net.Http;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Ollama;

await MainAsync(args);

async Task MainAsync(string[] args)
{
    string sheetId = Environment.GetEnvironmentVariable("SPREADSHEET_ID") ?? "1KsKmgmtiELtMvVFHkE9QLvt98Bb7LqJv_erA8OjTJJo";
    bool mockMode = !bool.TryParse(Environment.GetEnvironmentVariable("MOCK_MODE"), out var res) || res;

    Console.WriteLine("============================================================");
    Console.WriteLine("          C# LOCAL OLLAMA WORKSHEET WORK IQ AGENT           ");
    Console.WriteLine("============================================================");
    Console.WriteLine($"Spreadsheet ID : {sheetId}");
    Console.WriteLine($"Connection Mode: {(mockMode ? "[MOCK MODE]" : "[LIVE GOOGLE SHEETS]")}");
    Console.WriteLine("============================================================");

    // 1. Initialize the Local AI Kernel Brain
    var builder = Kernel.CreateBuilder();

    // Create custom HttpClient setting the BaseAddress as the endpoint and extending the timeout
    var httpClient = new HttpClient
    {
        BaseAddress = new Uri("http://localhost:11434"),
        Timeout = TimeSpan.FromMinutes(5) // Prevents the 100-second timeout exception
    };

#pragma warning disable SKEXP0070 
    builder.AddOllamaChatCompletion(
        modelId: "llama3.1",
        httpClient: httpClient
    );
#pragma warning restore SKEXP0070

    var kernel = builder.Build();

    // 2. Initialize layers and register DataLayer as an LLM Plugin
    var dataLayer = new DataLayer(sheetId, mockMode);
    var memoryLayer = new MemoryLayer();
    
    kernel.ImportPluginFromObject(dataLayer, "GoogleSheetsPlugin");

    Console.WriteLine("[Agent] Local Brain initialized with Google Sheet Tools. Ask me anything!\n");

    while (true)
    {
        Console.Write("You > ");
        string? userInput = Console.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(userInput)) continue;
        if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase) || userInput.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;

        Console.WriteLine("\n[Agent is thinking & invoking tools locally...]");

        try
        {
            string response = await ProcessAgentQueryAsync(kernel, userInput, memoryLayer);
            Console.WriteLine($"[Agent] {response}\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[Error] {ex.Message}\n");
        }
    }
}

// 3. Agentic Orchestration Layer
async Task<string> ProcessAgentQueryAsync(Kernel kernel, string query, MemoryLayer memory)
{
    var chatService = kernel.GetRequiredService<IChatCompletionService>();
    
    // Optimized strict prompt variant to force local model compliance
    var history = new ChatHistory(@"You are a strict data assistant for this specific Google Sheet. 

Rules:
1. ONLY answer questions using the data provided by your tools. Never use your internal general knowledge.
2. If the user asks a general question or makes small talk (e.g., 'Hello', 'What is a mouse?'), you MUST bypass tools and reply exactly: 'I am only authorized to answer questions regarding your spreadsheet data.'
3. If the user asks to look up an item but does not specify a tab, first call 'QuerySheetTabsAsync' to see what tabs exist, or ask the user which tab they want to search.");

    history.AddUserMessage(query);

    var settings = new OllamaPromptExecutionSettings 
    { 
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() 
    };

    var result = await chatService.GetChatMessageContentAsync(history, settings, kernel);
    
    memory.LogAgentAction("Local LLM Inference", $"Processed query via native loop.");
    return result.ToString();
}

// 4. Exposed Tools for the LLM Brain
public class DataLayer
{
    private string _sheetId;
    private bool _mockMode;
    public DataLayer(string sheetId, bool mockMode) { _sheetId = sheetId; _mockMode = mockMode; }

    [KernelFunction, Description("Fetches the list of all available tab names in the Google Sheet workspace.")]
    public async Task<List<string>> QuerySheetTabsAsync() 
    {
        Console.WriteLine("\n -> [Tool Triggered by Local LLM]: QuerySheetTabsAsync()");
        return await Task.FromResult(new List<string> { "Sales", "Inventory", "Users" });
    }

    [KernelFunction, Description("Searches a specific spreadsheet tab for rows matching a target item name.")]
    public async Task<string> SearchTabForValueAsync(
        [Description("The name of the tab to search (e.g., 'Inventory', 'Sales')")] string tabName, 
        [Description("The item name or keyword to look up")] string keyword)
    {
        Console.WriteLine($"\n -> [Tool Triggered by Local LLM]: SearchTabForValueAsync(Tab: {tabName}, Query: {keyword})");
        
        if (tabName.Equals("Inventory", StringComparison.OrdinalIgnoreCase) && keyword.Contains("mouse", StringComparison.OrdinalIgnoreCase))
        {
            return await Task.FromResult("Item Found: Wireless Mouse | Stock: 45 units | Status: In Stock | Location: Aisle 4");
        }
        return await Task.FromResult($"No precise row records found for '{keyword}' inside the '{tabName}' tab.");
    }
}

public class MemoryLayer
{
    public void LogAgentAction(string action, string details) => 
        Console.WriteLine($"[Memory Log] {action}: {details}");
}