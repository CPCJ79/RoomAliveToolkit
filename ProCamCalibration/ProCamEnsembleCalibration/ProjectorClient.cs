using System;
using System.Drawing;
using Grpc.Net.Client;
using RoomAlive.Grpc;

public class ProjectorServerClient : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly ProjectorServer.ProjectorServerClient _client;

    public ProjectorServerClient(string hostname, int port = 9000)
    {
        _channel = GrpcChannel.ForAddress($"http://{hostname}:{port}");
        _client = new ProjectorServer.ProjectorServerClient(_channel);
    }

    public void OpenDisplay(int screenIndex)
    {
        _client.OpenDisplay(new ScreenIndexRequest { ScreenIndex = screenIndex });
    }

    public Size Size(int screenIndex)
    {
        var reply = _client.Size(new ScreenIndexRequest { ScreenIndex = screenIndex });
        return new Size(reply.Width, reply.Height);
    }

    public int ScreenCount()
    {
        var reply = _client.ScreenCount(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Count;
    }

    public void SetColor(int screenIndex, float r, float g, float b)
    {
        _client.SetColor(new SetColorRequest
        {
            ScreenIndex = screenIndex,
            R = r,
            G = g,
            B = b
        });
    }

    public void DisplayName(int screenIndex, string name)
    {
        _client.DisplayName(new DisplayNameRequest
        {
            ScreenIndex = screenIndex,
            Name = name
        });
    }

    public int NumberOfGrayCodeImages(int screenIndex)
    {
        var reply = _client.NumberOfGrayCodeImages(new ScreenIndexRequest { ScreenIndex = screenIndex });
        return reply.Count;
    }

    public void DisplayGrayCode(int screenIndex, int index)
    {
        _client.DisplayGrayCode(new DisplayGrayCodeRequest
        {
            ScreenIndex = screenIndex,
            Index = index
        });
    }

    public void CloseDisplay(int screenIndex)
    {
        _client.CloseDisplay(new ScreenIndexRequest { ScreenIndex = screenIndex });
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}
