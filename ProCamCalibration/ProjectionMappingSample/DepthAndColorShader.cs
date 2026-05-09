using System;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using System.Numerics;

namespace RoomAliveToolkit
{
    public class DepthAndColorShader
    {
        public DepthAndColorShader(ID3D11Device device)
        {
            shaderByteCode = File.ReadAllBytes("Content/DepthAndColorFloatVS.cso");
            depthAndColorVS = device.CreateVertexShader(shaderByteCode);
            depthAndColorGS = device.CreateGeometryShader(File.ReadAllBytes("Content/DepthAndColorGS.cso"));
            depthAndColorPS = device.CreatePixelShader(File.ReadAllBytes("Content/DepthAndColorPS.cso"));

            // depth stencil state
            var depthStencilStateDesc = new DepthStencilDescription()
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual,
                StencilEnable = false,
            };
            depthStencilState = device.CreateDepthStencilState(depthStencilStateDesc);

            // rasterizer state
            var rasterizerStateDesc = new RasterizerDescription()
            {
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
                DepthClipEnable = true,
                FrontCounterClockwise = true,
                MultisampleEnable = true,
            };
            rasterizerState = device.CreateRasterizerState(rasterizerStateDesc);

            // color sampler state
            var colorSamplerStateDesc = new SamplerDescription()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Border,
                //BorderColor = new Color4(0.5f, 0.5f, 0.5f, 1.0f),
                BorderColor = new Color4(0, 0, 0, 1.0f),
            };
            colorSamplerState = device.CreateSamplerState(colorSamplerStateDesc);

            // filtered depth image
            var filteredDepthImageTextureDesc = new Texture2DDescription()
            {
                Width = (uint)(Kinect2Calibration.depthImageWidth * 3),
                Height = (uint)(Kinect2Calibration.depthImageHeight * 3),
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R32G32_Float,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
                CPUAccessFlags = CpuAccessFlags.None,
            };
            filteredDepthImageTexture = device.CreateTexture2D(filteredDepthImageTextureDesc);
            filteredRenderTargetView = device.CreateRenderTargetView(filteredDepthImageTexture);
            filteredDepthImageSRV = device.CreateShaderResourceView(filteredDepthImageTexture);

            filteredDepthImageTexture2 = device.CreateTexture2D(filteredDepthImageTextureDesc);
            filteredRenderTargetView2 = device.CreateRenderTargetView(filteredDepthImageTexture2);
            filteredDepthImageSRV2 = device.CreateShaderResourceView(filteredDepthImageTexture2);



            // constant buffer
            var constantBufferDesc = new BufferDescription()
            {
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (uint)ConstantBuffer.size,
                CPUAccessFlags = CpuAccessFlags.Write,
                StructureByteStride = 0,
                MiscFlags = 0,
            };
            constantBuffer = device.CreateBuffer(constantBufferDesc);

            bilateralFilter = new BilateralFilter(device, Kinect2Calibration.depthImageWidth, Kinect2Calibration.depthImageHeight);

            vertexInputLayout = device.CreateInputLayout(new[]
            {
                new InputElementDescription("SV_POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
            }, shaderByteCode);

        }

        public static ID3D11Buffer CreateVertexBuffer(ID3D11Device device, RoomAliveToolkit.Kinect2Calibration kinect2Calibration)
        {
            // generate depthFrameToCameraSpace table
            var depthFrameToCameraSpaceTable = kinect2Calibration.ComputeDepthFrameToCameraSpaceTable(Kinect2Calibration.depthImageWidth, Kinect2Calibration.depthImageHeight);


            int numVertices = 6 * (Kinect2Calibration.depthImageWidth - 1) * (Kinect2Calibration.depthImageHeight - 1);
            var vertices = new VertexPosition[numVertices];

            Int3[] quadOffsets = new Int3[]
            {
                new Int3(0, 0, 0),
                new Int3(1, 0, 0),
                new Int3(0, 1, 0),
                new Int3(1, 0, 0),
                new Int3(1, 1, 0),
                new Int3(0, 1, 0),
            };

            int vertexIndex = 0;
            for (int y = 0; y < Kinect2Calibration.depthImageHeight - 1; y++)
                for (int x = 0; x < Kinect2Calibration.depthImageWidth - 1; x++)
                    for (int i = 0; i < 6; i++)
                    {
                        int vertexX = x + quadOffsets[i].X;
                        int vertexY = y + quadOffsets[i].Y;

                        var point = depthFrameToCameraSpaceTable[Kinect2Calibration.depthImageWidth * vertexY + vertexX];

                        var vertex = new VertexPosition();
                        vertex.position = new Vector4(point.X, point.Y, vertexX, vertexY);
                        vertices[vertexIndex++] = vertex;
                    }

            var vertexBufferDesc = new BufferDescription()
            {
                BindFlags = BindFlags.VertexBuffer,
                CPUAccessFlags = CpuAccessFlags.None,
                Usage = ResourceUsage.Default,
                ByteWidth = (uint)(numVertices * VertexPosition.SizeInBytes),
            };
            var vertexBuffer = device.CreateBuffer(vertices, vertexBufferDesc);

            return vertexBuffer;
        }



        struct VertexPosition
        {
            public Vector4 position;
            static public int SizeInBytes { get { return 4 * 4;  } }
        }

        ID3D11InputLayout vertexInputLayout;

        // protip: compile shader with /Fc; output gives exact layout
        // hlsl matrices are stored column major
        // variables are stored on 4-component boundaries; inc. matrix columns
        // size is a multiple of 16
        [StructLayout(LayoutKind.Explicit, Size = ConstantBuffer.size)]
        unsafe struct ConstantBuffer
        {
            public const int size = 160;

            [FieldOffset(0)]
            public fixed float depthToColorTransform[16]; // 4-component padding
            [FieldOffset(64)]
            public fixed float f[2];
            [FieldOffset(72)]
            public fixed float c[2];
            [FieldOffset(80)]
            public float k1;
            [FieldOffset(84)]
            public float k2;
            [FieldOffset(96)]
            public fixed float projection[16];
        };


        public unsafe void SetConstants(ID3D11DeviceContext deviceContext, RoomAliveToolkit.Kinect2Calibration kinect2Calibration, Matrix4x4 projection)
        {
            // hlsl matrices are default column order
            var constants = new ConstantBuffer();
            for (int i = 0, col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    constants.projection[i] = projection[row, col];
                    constants.depthToColorTransform[i] = (float)kinect2Calibration.depthToColorTransform[row, col];
                    i++;
                }
            constants.f[0] = (float)kinect2Calibration.colorCameraMatrix[0, 0];
            constants.f[1] = (float)kinect2Calibration.colorCameraMatrix[1, 1];
            constants.c[0] = (float)kinect2Calibration.colorCameraMatrix[0, 2];
            constants.c[1] = (float)kinect2Calibration.colorCameraMatrix[1, 2];
            constants.k1 = (float)kinect2Calibration.colorLensDistortion[0];
            constants.k2 = (float)kinect2Calibration.colorLensDistortion[1];

            var mapped = deviceContext.Map(constantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(constantBuffer, 0);

        }


        public void Render(ID3D11DeviceContext deviceContext, ID3D11ShaderResourceView depthImageTextureRV, ID3D11ShaderResourceView colorImageTextureRV, ID3D11Buffer vertexBuffer, ID3D11RenderTargetView renderTargetView, ID3D11DepthStencilView depthStencilView, Viewport viewport)
        {
            deviceContext.IASetInputLayout(vertexInputLayout);
            deviceContext.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            deviceContext.IASetVertexBuffer(0, vertexBuffer, (uint)VertexPosition.SizeInBytes); // bytes per vertex
            deviceContext.RSSetState(rasterizerState);
            deviceContext.RSSetViewport(viewport);
            deviceContext.VSSetShader(depthAndColorVS);
            deviceContext.VSSetShaderResource(0, depthImageTextureRV);
            deviceContext.VSSetConstantBuffer(0, constantBuffer);
            deviceContext.GSSetShader(depthAndColorGS);
            deviceContext.PSSetShader(depthAndColorPS);
            deviceContext.PSSetShaderResource(0, colorImageTextureRV);
            deviceContext.PSSetSampler(0, colorSamplerState);
            deviceContext.OMSetRenderTargets(renderTargetView, depthStencilView);
            deviceContext.OMSetDepthStencilState(depthStencilState);
            deviceContext.Draw((Kinect2Calibration.depthImageWidth - 1) * (Kinect2Calibration.depthImageHeight - 1) * 6, 0);
        }

        ID3D11VertexShader depthAndColorVS;
        ID3D11GeometryShader depthAndColorGS;
        ID3D11PixelShader depthAndColorPS;
        byte[] shaderByteCode;
        ID3D11DepthStencilState depthStencilState;
        ID3D11RasterizerState rasterizerState;
        ID3D11SamplerState colorSamplerState;
        ID3D11Buffer constantBuffer;
        BilateralFilter bilateralFilter;
        ID3D11Texture2D filteredDepthImageTexture, filteredDepthImageTexture2;
        ID3D11RenderTargetView filteredRenderTargetView, filteredRenderTargetView2;
        ID3D11ShaderResourceView filteredDepthImageSRV, filteredDepthImageSRV2;
    }



}
