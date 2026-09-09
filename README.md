# Tuple Space Server

A simple console-based Tuple Space server using TCP and JSON.

## Features
- Create new tuple spaces.
- Add tuples to a space.
- Get (and remove) tuples from a space.
- Check if a tuple space is empty.
- Blocking `GET` requests: If a client requests a tuple from an empty space, the connection stays open and the client is notified as soon as a tuple is added.

## Protocol
The server communicates over TCP (default port 8080) using newline-delimited JSON messages.

### Commands
All commands should be a single JSON object per line.

#### CREATE
Creates a new tuple space or gets the existing one.
```json
{"Type": "CREATE", "SpaceName": "mySpace"}
```

#### ADD
Adds a tuple (array of strings) to a space.
```json
{"Type": "ADD", "SpaceName": "mySpace", "Tuple": ["key1", "value1", "extra"]}
```

#### GET
Gets and removes a tuple from a space. Blocks if the space is empty.
```json
{"Type": "GET", "SpaceName": "mySpace"}
```

#### ISEMPTY
Checks if a space is empty.
```json
{"Type": "ISEMPTY", "SpaceName": "mySpace"}
```

### Responses
The server responds with a single JSON object per line.

```json
{"Status": "OK"}
{"Status": "OK", "Tuple": ["key1", "value1"]}
{"Status": "OK", "IsEmpty": true}
{"Status": "Error", "Message": "Unknown command"}
```

## Running the Server
```bash
dotnet run --project TupleServer
```

## Running Tests
```bash
dotnet test
```
