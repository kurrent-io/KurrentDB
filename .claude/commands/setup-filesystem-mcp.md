---
description: "Configure filesystem MCP server with correct project paths"
argument-hint: "No arguments required"
allowed-tools: ["Bash", "Write", "Read", "Edit"]
---

# Setup Filesystem MCP Server

Configure the filesystem MCP server for this KurrentDB project with the correct local paths.

## Task Steps:

1. **Detect Project Root**: Use bash to get the current working directory and confirm it's the KurrentDB project root by checking for key files (src/, proto/, KurrentDB.sln, etc.)

2. **Create MCP Configuration**: Create or update `.mcp.json` file in the project root with the filesystem server configuration using the detected project path

3. **Configure Paths**: Set up filesystem server to access:
   - Project root directory
   - `src/` directory (main source code)
   - `docs/` directory (if it exists)  
   - `proto/` directory (protocol buffers)

4. **Verify Configuration**: Check that the configuration is valid and provide feedback on successful setup

5. **Usage Instructions**: Explain how to use the filesystem MCP server after setup

The configuration should use absolute paths to ensure reliability and follow MCP best practices. After setup, users will have access to filesystem operations through the MCP server.