using System;
using System.Runtime.InteropServices;
using Google.Protobuf;
using Grpc.Net.Client;
using RoomAlive.Grpc;
using RoomAliveToolkit;

public class KinectServer2Client : IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly KinectServer2.KinectServer2Client _client;

    public KinectServer2Client(string hostname, int port = 9000)
    {
        _channel = GrpcChannel.ForAddress($"http://{hostname}:{port}");
        _client = new KinectServer2.KinectServer2Client(_channel);
    }

    public byte[] LatestDepthImage()
    {
        var reply = _client.LatestDepthImage(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Data.ToByteArray();
    }

    public byte[] LatestYUVImage()
    {
        var reply = _client.LatestYUVImage(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Data.ToByteArray();
    }

    public byte[] LatestRGBImage()
    {
        var reply = _client.LatestRGBImage(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Data.ToByteArray();
    }

    public byte[] LatestJPEGImage()
    {
        var reply = _client.LatestJPEGImage(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Data.ToByteArray();
    }

    public float LastColorGain()
    {
        var reply = _client.LastColorGain(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Gain;
    }

    public long LastColorExposureTimeTicks()
    {
        var reply = _client.LastColorExposureTimeTicks(new Google.Protobuf.WellKnownTypes.Empty());
        return reply.Ticks;
    }

    public Kinect2Calibration GetCalibration()
    {
        var reply = _client.GetCalibration(new Google.Protobuf.WellKnownTypes.Empty());

        var calibration = new Kinect2Calibration();
        calibration.colorCameraMatrix = DeserializeMatrix(reply.ColorCameraMatrix.ToByteArray(), 3, 3);
        calibration.colorLensDistortion = DeserializeMatrix(reply.ColorLensDistortion.ToByteArray(), 2, 1);
        calibration.depthCameraMatrix = DeserializeMatrix(reply.DepthCameraMatrix.ToByteArray(), 3, 3);
        calibration.depthLensDistortion = DeserializeMatrix(reply.DepthLensDistortion.ToByteArray(), 2, 1);
        calibration.depthToColorTransform = DeserializeMatrix(reply.DepthToColorTransform.ToByteArray(), 4, 4);

        return calibration;
    }

    private static Matrix DeserializeMatrix(byte[] bytes, int rows, int cols)
    {
        var matrix = new Matrix(rows, cols);
        var doubles = new double[rows * cols];
        Buffer.BlockCopy(bytes, 0, doubles, 0, bytes.Length);
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
                matrix[i, j] = doubles[i * cols + j];
        return matrix;
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}
