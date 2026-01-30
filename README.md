# Csat

## TCP chat client (bad)

### Usage:

Server:

```console
$ dotnet run --project Server
```

Clients:

```console
$ dotnet run --project csat

# Stress test / Ban system
$ yes | nc -s 127.0.0.1 -p <port> 127.0.0.1 4293
```
