using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace RoomAliveToolkit
{
    public class FilterPixelShader
    {
        public FilterPixelShader(ID3D11Device device, int imageWidth, int imageHeight, int constantBufferSize, string pixelShaderBytecodeFilename)
        {
            vertexShader = device.CreateVertexShader(File.ReadAllBytes("Content/FullScreenQuadVS.cso"));
            pixelShader = device.CreatePixelShader(File.ReadAllBytes(pixelShaderBytecodeFilename));

            var rasterizerStateDesc = new RasterizerDescription()
            {
                CullMode = CullMode.None,
                FillMode = FillMode.Solid,
                DepthClipEnable = false,
                FrontCounterClockwise = true,
                MultisampleEnable = false,
            };
            rasterizerState = device.CreateRasterizerState(rasterizerStateDesc);

            if (constantBufferSize > 0)
            {
                var constantBufferDesc = new BufferDescription()
                {
                    Usage = ResourceUsage.Dynamic,
                    BindFlags = BindFlags.ConstantBuffer,
                    ByteWidth = (uint)constantBufferSize,
                    CPUAccessFlags = CpuAccessFlags.Write,
                    StructureByteStride = 0,
                    MiscFlags = 0,
                };
                constantBuffer = device.CreateBuffer(constantBufferDesc);
            }

            viewport = new Viewport(0, 0, imageWidth, imageHeight);
        }

        public virtual void Render(ID3D11DeviceContext deviceContext, ID3D11ShaderResourceView inputRV, ID3D11RenderTargetView renderTargetView)
        {
            deviceContext.IASetVertexBuffer(0, null, 0);
            deviceContext.IASetInputLayout(null);
            deviceContext.IASetPrimitiveTopology(Vortice.Direct3D.PrimitiveTopology.TriangleStrip);
            deviceContext.OMSetRenderTargets(renderTargetView);
            deviceContext.RSSetState(rasterizerState);
            deviceContext.RSSetViewport(viewport);
            deviceContext.VSSetShaderResource(0, null); // TODO: this should be done by the depthAndColorVS
            deviceContext.VSSetShader(vertexShader);
            deviceContext.GSSetShader(null);
            deviceContext.PSSetShader(pixelShader);
            deviceContext.PSSetShaderResource(0, inputRV);
            if (constantBuffer != null)
                deviceContext.PSSetConstantBuffer(0, constantBuffer);
            deviceContext.Draw(4, 0);
            ID3D11RenderTargetView nullRTV = null;
            deviceContext.OMSetRenderTargets(nullRTV);
            deviceContext.PSSetShaderResource(0, null);
        }

        ID3D11VertexShader vertexShader;
        ID3D11PixelShader pixelShader;
        ID3D11RasterizerState rasterizerState;
        public Viewport viewport;
        protected ID3D11Buffer constantBuffer;
    }

    public class FromUIntPS : FilterPixelShader
    {
        public FromUIntPS(ID3D11Device device, int imageWidth, int imageHeight)
            : base(device, imageWidth, imageHeight, 0, "Content/FromUIntPS.cso")
        {
        }
    }


    public class PassThrough : FilterPixelShader
    {
        //TODO: maybe just put sampler in base class
        public PassThrough(ID3D11Device device, int imageWidth, int imageHeight)
            : base(device, imageWidth, imageHeight, 0, "Content/PassThroughPS.cso")
        {
            var samplerStateDesc = new SamplerDescription()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Border,
                //BorderColor = new Color4(0.5f, 0.5f, 0.5f, 1.0f),
                BorderColor = new Color4(0, 0, 0, 1.0f),
            };
            samplerState = device.CreateSamplerState(samplerStateDesc);
        }
        public override void Render(ID3D11DeviceContext deviceContext, ID3D11ShaderResourceView inputRV, ID3D11RenderTargetView renderTargetView)
        {
            deviceContext.PSSetSampler(0, samplerState);

            base.Render(deviceContext, inputRV, renderTargetView);
        }

        ID3D11SamplerState samplerState;
    }

    public class RadialWobble : FilterPixelShader
    {
        public RadialWobble(ID3D11Device device, int imageWidth, int imageHeight)
            : base(device, imageWidth, imageHeight, constantBufferSize, "Content/RadialWobblePS.cso")
        {
            var samplerStateDesc = new SamplerDescription()
            {
                Filter = Filter.MinMagMipLinear,
                AddressU = TextureAddressMode.Border,
                AddressV = TextureAddressMode.Border,
                AddressW = TextureAddressMode.Border,
                //BorderColor = new Color4(0.5f, 0.5f, 0.5f, 1.0f),
                BorderColor = new Color4(0, 0, 0, 1.0f),
            };
            samplerState = device.CreateSamplerState(samplerStateDesc);

            SetConstants(device.ImmediateContext, 0);
        }
        public override void Render(ID3D11DeviceContext deviceContext, ID3D11ShaderResourceView inputRV, ID3D11RenderTargetView renderTargetView)
        {
            deviceContext.PSSetSampler(0, samplerState);

            base.Render(deviceContext, inputRV, renderTargetView);
        }

        ID3D11SamplerState samplerState;

        const int constantBufferSize = 16; // must be multiple of 16

        public void SetConstants(ID3D11DeviceContext deviceContext, float newAlpha)
        {
            Constants constants = new Constants()
            {
                alpha = newAlpha,
            };

            var mapped = deviceContext.Map(constantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(constantBuffer, 0);
        }


        // protip: compile shader with /Fc; output gives exact layout
        // hlsl matrices are stored column major
        // variables are stored on 4-component boundaries; inc. matrix columns
        // size is a multiple of 16
        [StructLayout(LayoutKind.Explicit, Size = constantBufferSize)]
        public unsafe struct Constants
        {
            [FieldOffset(0)]
            public float alpha;
        };
    }



    public class BilateralFilter : FilterPixelShader
    {
        public BilateralFilter(ID3D11Device device, int imageWidth, int imageHeight)
            : base(device, imageWidth, imageHeight, constantBufferSize, "Content/BilateralFilterPS.cso")
        {
            SetConstants(device.ImmediateContext, 4f, 100f);
        }

        public void SetConstants(ID3D11DeviceContext deviceContext, float newSpatialSigma, float newIntensitySigma)
        {
            Constants constants = new Constants()
            {
                spatialSigma = 1f/newSpatialSigma,
                intensitySigma = 1f/newIntensitySigma,
            };

            var mapped = deviceContext.Map(constantBuffer, MapMode.WriteDiscard);
            Marshal.StructureToPtr(constants, mapped.DataPointer, false);
            deviceContext.Unmap(constantBuffer, 0);
        }

        const int constantBufferSize = 16; // must be multiple of 16

        // protip: compile shader with /Fc; output gives exact layout
        // hlsl matrices are stored column major
        // variables are stored on 4-component boundaries; inc. matrix columns
        // size is a multiple of 16
        [StructLayout(LayoutKind.Explicit, Size = constantBufferSize)]
        public unsafe struct Constants
        {
            [FieldOffset(0)]
            public float spatialSigma;
            [FieldOffset(4)]
            public float intensitySigma;
        };
    }

}
