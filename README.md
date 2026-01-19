# CSAT

## TCP chat client (bad)

### Usage:

Server:
```console
$ dotnet run
```

Clients:
```console
$ telnet 127.0.0.1 6969
# OR
$ nc 127.0.0.1 6969

# Stress test / bann system
$ yes | nc -s 127.0.0.1 -p <port> 127.0.0.1 6969
```
