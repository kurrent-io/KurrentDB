# Claude Code MCP Configuration

This directory contains MCP (Model Context Protocol) server configurations for the KurrentDB project.

## Configured MCP Servers

### Microsoft Docs MCP Server
- **Purpose**: Access official Microsoft and Azure documentation
- **Available Operations**:
  - `microsoft_docs_search` - Search Microsoft documentation
  - `microsoft_docs_fetch` - Fetch complete documentation pages

### Context7 MCP Server  
- **Purpose**: Access library documentation and code examples
- **Available Operations**:
  - `resolve-library-id` - Find library IDs for packages/frameworks
  - `get-library-docs` - Get comprehensive library documentation

### Filesystem MCP Server
- **Purpose**: Direct file system access to project files
- **Available Operations**:
  - `read_text_file` - Read files as text with optional head/tail limits
  - `read_media_file` - Read image/audio files (returns base64 data)
  - `read_multiple_files` - Read multiple files simultaneously
- **Access Scope**: Project root, src/, docs/, and proto/ directories
- **Configuration**: Use `.mcp.json` for local filesystem paths (see setup below)

## Files

- `settings.json` - Project-level MCP server configurations (committed to repo)
- `settings.local.json` - Local user-specific permissions (not committed)
- `.mcp.json` - Local MCP server configurations with your specific paths (not committed)
- `.mcp.json.example` - Template for local MCP configuration (committed to repo)

## Configured Permissions

This project grants full access to commonly used Claude Code tools:

### Core Tools
- **Bash** - Execute shell commands and scripts
- **Edit/MultiEdit** - Modify files in the codebase
- **Read/Write** - Read and create files
- **Glob/Grep** - Search and pattern matching
- **WebSearch/WebFetch** - Access web resources
- **Task** - Launch specialized agents
- **TodoWrite** - Task management and planning

### Development Tools  
- **NotebookEdit** - Jupyter notebook editing
- **BashOutput/KillBash** - Background process management
- **ExitPlanMode** - Development planning workflow

### MCP Integrations
- **Filesystem operations** - Directory trees and file operations
- **Context7** - Library documentation access
- **Microsoft Docs** - Official Microsoft documentation
- **Brave Search** - Enhanced web search capabilities
- **IDE integration** - Development environment tools

## Setup

When you clone this repository:

### Automatic Setup
Claude Code should automatically:
1. Detect the `.claude/settings.json` configuration
2. Install the required MCP servers via npm
3. Enable all configured tools and permissions
4. Make the servers and tools available for immediate use

### Local Filesystem Configuration
To enable the filesystem MCP server with your local paths, use the custom slash command:

1. **Run the setup command**: Type `/setup-filesystem-mcp` in Claude Code
2. The command will automatically:
   - Detect your project root directory
   - Create or update `.mcp.json` with correct absolute paths
   - Configure access to project root, src/, docs/, and proto/ directories
   - Verify the configuration is working

**Alternative manual setup** (if needed):
1. Copy `.mcp.json.example` to `.mcp.json`
2. Replace `PROJECT_ROOT_PATH` with your absolute project path
3. Claude Code will automatically detect and use the `.mcp.json` configuration

## Usage

Once configured, you can use these MCP servers directly in Claude Code:

```
# Search Microsoft docs
Can you search for ASP.NET Core authentication?

# Get library documentation  
Can you get documentation for the Entity Framework library?

# Access project files directly
Can you read the KurrentDB.Testing.csproj file?

# Read multiple files at once
Can you read all the proto files in the proto/ directory?
```

The servers provide access to up-to-date documentation and examples without needing to manually search online.