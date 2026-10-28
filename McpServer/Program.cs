using McpServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

// =============================================================================
// HR Tools MCP Server
//
// LEARNING NOTE — What this is
// This is a standalone process that speaks the Model Context Protocol.
// It exposes 3 HR tools over stdio so any MCP-compatible client can use them:
//   - Claude Desktop
//   - Cursor IDE
//   - Our own RAG app (via MCP client)
//   - Any future AI app
//
// Transport: stdio (stdin/stdout)
// The client launches this process and communicates by writing JSON to stdin
// and reading JSON responses from stdout.
//
// LEARNING NOTE — Why a separate process?
// MCP servers are intentionally isolated processes. This means:
//   1. Any language/runtime can host a server (Python, Node, .NET, Go...)
//   2. The server can be deployed independently and versioned separately
//   3. Multiple AI apps share one server without code duplication
// =============================================================================

var builder = Host.CreateApplicationBuilder(args);

// Load config from appsettings.json or environment variables
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables();

// Inject config into our static tool class
var config = builder.Configuration;
HrTools.ConnectionString = config.GetConnectionString("RAGDemo")
    ?? throw new InvalidOperationException("ConnectionStrings:RAGDemo not configured");
HrTools.OpenAiApiKey = config["OpenAI:ApiKey"]
    ?? throw new InvalidOperationException("OpenAI:ApiKey not configured");

// Register the MCP server with stdio transport
// WithToolsFromAssembly() scans for all [McpServerTool] methods automatically
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();
