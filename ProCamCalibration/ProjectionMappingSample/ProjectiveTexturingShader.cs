using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;

namespace RoomAliveToolkit
{
    public class ProjectiveTexturingShader
    {
        public ProjectiveTexturingShader(ID3D11Device device)
        {
            var shaderByteCode = File.ReadAllBytes("Content/DepthAndProjectiveTextureVS.cso");
            vertexShader = device.CreateVertexShader(shaderByteCode);
            geometryShader = device.CreateGeometryShader(File.ReadAllBytes("Content/DepthAndColorGS.cso"));
            pixelShader = device.CreatePixelShader(File.ReadAllBytes("Content/DepthAndColorPS.cso"));

            // depth stencil state
            var depthStencilStateDesc = new DepthStencilDescription()
            {
                DepthEnable = true,
                DepthWriteMask = DepthWriteMask.All,
                DepthFunc = ComparisonFunction.LessEqual,
                StencilEnable = false,
            };
            depthStencilState = device.CreateDepthStencilState(depthStencilStateDesc);

            // rasterizer states
            var rasterizerStateDesc = new RasterizerDescription()
            {
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
                DepthClipEnable = true,
                FrontCounterClockwise = true,
                MultisampleEnable = true,
            };
            rasterizerState = device.CreateRasterizerState(rasterizerStateDesc);

            // constant buffer
            var constantBufferDesc = new BufferDescription()
            {
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ConstantBuffer,
                ByteWidth = (uint)Constants.size,
                CPUAccessFlags = CpuAccessFlags.Write,
                StructureByteStride = 0,
                MiscFlags = 0,
            };
            constantBuffer = device.CreateBuffer(constantBufferDesc);

            // user view sampler state
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

            vertexInputLayout = device.CreateInputLayout(new[]
            {
                new InputElementDescription("SV_POSITION", 0, Format.R32G32B32A32_Float, 0, 0),
            }, shaderByteCode);

        }

        // protip: compile shader with /Fc; output gives exact layout
        // hlsl matrices are stored column major
        // variables are stored on 4-component boundaries; inc. matrix columns
        // size is a multiple of 16
        [StructLayout(LayoutKind.Explicit, Size = Constants.size)]
        public unsafe struct Constants
        {
            public const int size = 128;
            [FieldOffset(0)]
            public fixed float userWorldViewProjection[16];
            [FieldOffset(64)]
            public fixed float projectorWorldViewProjection[16];
        };

        public unsafe void SetConstants(ID3D11DeviceContext deviceContext, Matrix4x4 userWorldViewProjection, Matrix4x4 projectorWorldViewProjection)
        {
            Constants constants = new Constants();

            for (int i = 0, col = 0; col < 4; col++)
                for (int row = 0; row < 4; row++)
                {
                    constants.userWorldViewProjection[i] = userWorldViewProjection[row, col];
                    constants.projectorWorldViewProjection[i] = projectorWorldViewProjection[row, col];
                    i++;
                }

            var mapped = deviceContext.Map(constantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(constantBuffer, 0);
        }

        public void Render(ID3D11DeviceContext deviceContext, ID3D11ShaderResourceView depthImageTextureRV, ID3D11ShaderResourceView colorImageTextureRV, ID3D11Buffer vertexBuffer, ID3D11RenderTargetView renderTargetView, ID3D11DepthStencilView depthStencilView, Viewport viewport)
        {
            deviceContext.IASetInputLayout(vertexInputLayout);
            deviceContext.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleList);
            deviceContext.IASetVertexBuffer(0, vertexBuffer, 16u); // bytes per vertex
            deviceContext.OMSetRenderTargets(renderTargetView, depthStencilView);
            deviceContext.OMSetDepthStencilState(depthStencilState);
            deviceContext.RSSetState(rasterizerState);
            deviceContext.RSSetViewport(viewport);
            deviceContext.VSSetShader(vertexShader);
            deviceContext.VSSetShaderResource(0, depthImageTextureRV);
            deviceContext.VSSetConstantBuffer(0, constantBuffer);
            deviceContext.GSSetShader(geometryShader);
            deviceContext.PSSetShader(pixelShader);
            deviceContext.PSSetShaderResource(0, colorImageTextureRV);
            deviceContext.PSSetSampler(0, colorSamplerState);
            deviceContext.Draw((Kinect2Calibration.depthImageWidth - 1) * (Kinect2Calibration.depthImageHeight - 1) * 6, 0);

            deviceContext.VSSetShaderResource(0, null); // to avoid warnings when these are later set as render targets
            deviceContext.PSSetShaderResource(0, null);
        }

        ID3D11VertexShader vertexShader;
        ID3D11GeometryShader geometryShader;
        ID3D11PixelShader pixelShader;
        ID3D11DepthStencilState depthStencilState;
        ID3D11RasterizerState rasterizerState;
        ID3D11Buffer constantBuffer;
        ID3D11SamplerState colorSamplerState;
        ID3D11InputLayout vertexInputLayout;
    }
}
