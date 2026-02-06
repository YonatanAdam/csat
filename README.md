# csat

A lightweight TCP-based chat application built in C# featuring a WPF client and async server with rate limiting and anti-spam measures.

## Overview

**csat** is a learning project demonstrating:

- TCP socket programming in C#
- Asynchronous networking with `async/await`
- WPF desktop application development
- Server-side message handling and client management
- Rate limiting and ban systems for abuse prevention

## Features

- **Multi-client Chat Server**: Handle multiple concurrent connections
- **WPF Client GUI**: Clean interface for sending/receiving messages
- **Rate Limiting**: Configurable message rate to prevent spam
- **Ban System**: Automatic banning after exceeding strike limits
- **Command System**: Built-in commands like `/connect`, `/disconnect`, `/help`
- **Configurable Globals**: Easy tuning of ports, timeouts, and rate limits
- **Logging**: Debug and info logging with configurable levels

## Architecture

```
csat/
├── src/
│   ├── csat/              # WPF Client Application
│   │   ├── MainWindow.xaml.cs   # Client UI and connection logic
│   │   └── App.xaml.cs
│   ├── Server/            # Chat Server
│   │   ├── ChatServer.cs        # Main server implementation
│   │   ├── MessageHandler.cs    # Message routing
│   │   ├── AdminCommandHandler.cs # Admin commands
│   │   └── Client.cs            # Client state management
│   └── Utils/             # Shared Utilities
│       ├── Global.cs            # Configuration constants
│       ├── CommandParser.cs      # Command parsing logic
│       ├── ILogger.cs & ConsoleLogger.cs # Logging
│       └── Sensitive.cs          # Sensitive data handling
├── global.json            # .NET version specification
└── csat.sln              # Visual Studio solution
```

## Getting Started

### Prerequisites

- .NET 8.0+ SDK (check `global.json` for the exact version)
- Visual Studio 2022 or Visual Studio Code (optional, CLI works too)
- Windows (for WPF GUI)

### Build

```bash
git clone https://github.com/YonatanAdam/csat.git
cd csat
dotnet build
```

### Run

**Start the server:**

```bash
dotnet run --project src/Server
```

**Start client(s):**

```bash
dotnet run --project src/csat
```

Once the client launches, use `/connect <server-ip>` to connect (default is `127.0.0.1`).

## Usage

### Client Commands

| Command      | Usage           | Description                             |
| ------------ | --------------- | --------------------------------------- |
| `connect`    | `/connect <ip>` | Connect to a server at the specified IP |
| `disconnect` | `/disconnect`   | Disconnect from the current server      |
| `help`       | `/help` or `/h` | Display available commands              |
| `clear`      | `/clear`        | Clear chat history                      |
| `ping`       | `/ping`         | Test connection (replies with "Pong")   |
| `users`      | `/users`        | List connected users (unimplemented)    |
| `exit`       | `/exit`         | Close the application                   |

## Stress Testing

Test the rate limiting and ban system:

```bash
yes | nc -s 127.0.0.1 -p <port> 127.0.0.1 4293
```

## Limitations

- `/users` command not yet implemented
- Client UI lacks advanced features (user list, private messages)
- No persistent message history
- Local network only (no port forwarding setup guide)

## Roadmap

- [ ] Implement `/users` command to list online users
- [ ] Add encrypted communication (TLS/SSL)
- [ ] Message history persistence
- [ ] Private messaging support
- [ ] Better error handling and recovery

## Notes

Don't send naughty stuff, ban people can see!
