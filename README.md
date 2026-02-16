# IPTV Backend - Stalker Portal Manager

A C# ASP.NET Core IPTV backend service that provides a simple interface to manage Stalker portal subscriptions and proxy streams to avoid CORS issues in the browser.

## Features

- **Portal Management**: Add, edit, test, and delete Stalker portal subscriptions
- **Channel Browser**: View and search channels from connected portals
- **Streaming Proxy**: Stream channels through the backend to bypass CORS restrictions
- **JSON Storage**: Simple file-based storage for portal configurations
- **Modern Web UI**: Clean, responsive interface for managing your IPTV setup

## Quick Start

### Prerequisites

- .NET 8.0 SDK or later
- Modern web browser

### Installation

1. Clone or download the project
2. Navigate to the project directory:
   ```bash
   cd IptvBackend
   ```

3. Run the application:
   ```bash
   dotnet run
   ```

4. Open your browser and navigate to:
   - http://localhost:5000 (HTTP)
   - https://localhost:5001 (HTTPS)

### Usage

1. **Add a Portal**: Click "Portals" tab and fill in your Stalker portal details:
   - **Name**: A friendly name for your portal
   - **Portal URL**: Your Stalker portal URL (e.g., `http://portal.example.com:8080`)
   - **MAC Address**: Your STB MAC address (e.g., `00:1A:79:XX:XX:XX`)

2. **Test Connection**: Click "Test" to verify the portal works

3. **Browse Channels**: Go to "Channels" tab, select your portal, and browse available channels

4. **Stream**: Click any channel to start watching. Streams are automatically proxied through the backend

## API Endpoints

### Portals
- `GET /api/portals` - List all portals
- `POST /api/portals` - Add new portal
- `GET /api/portals/{id}` - Get portal details
- `PUT /api/portals/{id}` - Update portal
- `DELETE /api/portals/{id}` - Delete portal
- `POST /api/portals/{id}/test` - Test portal connection

### Channels
- `GET /api/channels/portal/{portalId}` - Get channels from portal
- `GET /api/channels/portal/{portalId}/categories` - Get available categories
- `GET /api/channels/portal/{portalId}/search?q={query}` - Search channels

### Streaming Proxy
- `GET /api/proxy/stream?portalId={id}&channelId={id}` - Stream a channel through proxy
- `GET /api/proxy/url?url={encoded_url}` - Proxy any media URL

## Technical Details

### Stalker Portal Authentication

The backend handles Stalker portal authentication including:
- MAC address-based device identification
- Token-based session management
- Device ID generation (serial, device_id, device_id2, signature)
- Proper HTTP headers for Stalker API compatibility

### Streaming Proxy

The proxy service:
- Handles CORS by proxying streams through the backend
- Supports multiple stream formats (MPEG-TS, HLS, MP4)
- Maintains proper HTTP headers for streaming
- Handles range requests for seeking
- Streams efficiently with proper buffering

### Storage

- Portal data stored in JSON files in `data/portals.json`
- Automatic file creation and management
- Thread-safe access with locking
- Atomic save operations

## Project Structure

```
IptvBackend/
 Controllers/           # API controllers
   ├── PortalsController.cs
   ├── ChannelsController.cs
   └── ProxyController.cs
 Models/               # Data models
   ├── Portal.cs
   ├── Channel.cs
   └── StalkerResponse.cs
 Services/             # Business logic
   ├── PortalStore.cs
   ├── StalkerPortalClient.cs
   └── StreamProxyService.cs
 wwwroot/             # Static files
   ├── index.html
   ├── css/styles.css
   └── js/app.js
 Program.cs           # Application configuration
```

## Configuration

Edit `appsettings.json` to customize:

```json
{
  "PortalSettings": {
    "DataPath": "./data",
    "TokenTtlMinutes": 600
  },
  "ProxySettings": {
    "BufferSize": 81920,
    "TimeoutSeconds": 300
  }
}
```

## Security Considerations

- Portal credentials are stored locally in JSON files
- The proxy service allows CORS from any origin (for development)
- API endpoints are not authenticated (implement auth as needed)
- MAC addresses are sensitive data - treat appropriately

## Troubleshooting

### Portal Connection Issues
- Verify the portal URL is correct and accessible
- Ensure MAC address format is valid (00:1A:79:XX:XX:XX)
- Check firewall settings for outbound connections

### Streaming Issues
- Some portals may use different stream formats
- Check browser console for errors
- Try different browsers or disable browser extensions

### Performance
- The streaming proxy buffers data in memory
- For production use, consider implementing:
  - Authentication
  - Rate limiting
  - Log aggregation
  - Better error handling

## Development

This project is built with:
- ASP.NET Core 8.0
- C# 12
- HTML5/CSS3/JavaScript (ES6+)
- HTTP client factory
- JSON serialization

## License

This project is provided as-is for educational and personal use. Ensure you comply with your IPTV provider's terms of service and applicable laws when using this software.
