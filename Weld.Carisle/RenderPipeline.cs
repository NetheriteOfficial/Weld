using System.Numerics;
using Silk.NET.OpenGL;
using Weld.Carisle.Formats;
using Weld.Carisle.Rendering;
using Weld.Entities;
using Weld.World;
using Weld.World.Streaming;

namespace Weld.Carisle
{
    internal sealed class RenderPipeline : IDisposable
    {
        public RenderPipeline(GL gl, Level level)
        {
            _gl = gl;
            _level = level;
            InitializeOffscreenSwapchain();
            _resources = new GpuResourceCache(gl, level);
            _sceneShader = new ShaderProgram(gl, Shaders.SceneVertex, Shaders.SceneFragment);
            _upscaleShader = new ShaderProgram(gl, Shaders.UpscaleVertex, Shaders.UpscaleFragment);
            _emptyVao = _gl.CreateVertexArray();
            _entities = new EntityRenderer(gl);

            _uModel = _sceneShader.Uniform("uModel");
            _uViewProj = _sceneShader.Uniform("uViewProj");
            _uDiffuse = _sceneShader.Uniform("uDiffuse");
            _uHasTexture = _sceneShader.Uniform("uHasTexture");
            _uLightDir = _sceneShader.Uniform("uLightDir");
            _uFogColor = _sceneShader.Uniform("uFogColor");
            _uFogRange = _sceneShader.Uniform("uFogRange");
            _uCameraPos = _sceneShader.Uniform("uCameraPos");
            _uTexture = _sceneShader.Uniform("uTexture");
            _uOpacity = _sceneShader.Uniform("uOpacity");
            _uDitherInvert = _sceneShader.Uniform("uDitherInvert");

            _uSource = _upscaleShader.Uniform("uSource");
            _uSourceSize = _upscaleShader.Uniform("uSourceSize");
            _uOutputSize = _upscaleShader.Uniform("uOutputSize");
            _uScaleOffset = _upscaleShader.Uniform("uScaleOffset");
            _uFilter = _upscaleShader.Uniform("uFilter");
        }

        private readonly GL _gl;
        private readonly Level _level;
        private readonly GpuResourceCache _resources;
        private readonly EntityRenderer _entities;
        private readonly ShaderProgram _sceneShader, _upscaleShader;
        private readonly uint _emptyVao;
        private readonly int _uModel, _uViewProj, _uDiffuse, _uHasTexture, _uLightDir, _uFogColor, _uFogRange, _uCameraPos, _uTexture, _uOpacity, _uDitherInvert;
        private readonly int _uSource, _uSourceSize, _uOutputSize, _uScaleOffset, _uFilter;
        private uint _replacementSwapchainFramebuffer, _swapchainColorTexture, _swapchainDepthRenderbuffer;

        public const uint InternalWidth = 640;
        public const uint InternalHeight = 448;
        public static float InternalAspect => (float)InternalWidth / InternalHeight;

        public UpscaleFilter Filter { get; set; } = UpscaleFilter.SharpBilinear;
        public bool PreserveAspectRatio { get; set; } = true;
        public bool UseBlitPresent { get; set; }
        public Vector4 ClearColor { get; set; } = new(0.2f, 0.6f, 1f, 1f);
        public Vector3 LightDirection { get; set; } = Vector3.Normalize(new Vector3(0.4f, 0.3f, 1f));
        public Vector3 FogColor { get; set; } = new(0.2f, 0.6f, 1f);
        public Vector2 FogRange { get; set; } = new(150f, 480f);
        public bool Wireframe { get; set; }
        public bool DitherFades { get; set; } = true;

        public int DrawCallsLastFrame { get; private set; }
        public int TrianglesLastFrame { get; private set; }
        public int EntitiesLastFrame => _entities.DrawnLastFrame;

        private void InitializeOffscreenSwapchain()
        {
            _replacementSwapchainFramebuffer = _gl.CreateFramebuffer();

            _swapchainColorTexture = _gl.CreateTexture(TextureTarget.Texture2D);
            _gl.TextureStorage2D(_swapchainColorTexture, 1, SizedInternalFormat.Rgba8, InternalWidth, InternalHeight);

            _gl.TextureParameter(_swapchainColorTexture, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
            _gl.TextureParameter(_swapchainColorTexture, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
            _gl.TextureParameter(_swapchainColorTexture, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
            _gl.TextureParameter(_swapchainColorTexture, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);

            _gl.NamedFramebufferTexture(_replacementSwapchainFramebuffer, FramebufferAttachment.ColorAttachment0, _swapchainColorTexture, 0);

            _swapchainDepthRenderbuffer = _gl.CreateRenderbuffer();
            _gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _swapchainDepthRenderbuffer);
            _gl.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.Depth24Stencil8, InternalWidth, InternalHeight);
            _gl.NamedFramebufferRenderbuffer(_replacementSwapchainFramebuffer, FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _swapchainDepthRenderbuffer);

            var status = _gl.CheckNamedFramebufferStatus(_replacementSwapchainFramebuffer, FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete)
            {
                throw new Exception($"FBO creation failed: {status}");
            }
        }

        public void RenderFrame(in StreamingCameraData camera, int windowWidth, int windowHeight, GameWorld? world = null, float alpha = 1f)
        {
            BeginFrame();
            RenderLevel(camera);
            if (world != null) RenderEntities(world, alpha);
            Present(windowWidth, windowHeight);
        }

        public unsafe void RenderEntities(GameWorld world, float alpha)
        {
            _sceneShader.Use();
            _gl.Enable(EnableCap.DepthTest);
            _entities.Draw(world, alpha, _uModel, _uDiffuse, _uHasTexture, _uOpacity, _uDitherInvert);
        }

        public void BeginFrame()
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _replacementSwapchainFramebuffer);
            _gl.Viewport(0, 0, InternalWidth, InternalHeight);
            _gl.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, ClearColor.W);
            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            DrawCallsLastFrame = 0;
            TrianglesLastFrame = 0;
        }

        public unsafe void RenderLevel(in StreamingCameraData camera)
        {
            _gl.Enable(EnableCap.DepthTest);
            _gl.DepthFunc(DepthFunction.Less);
            _gl.Disable(EnableCap.CullFace);
            _gl.Disable(EnableCap.Blend);
            _gl.PolygonMode(GLEnum.FrontAndBack, Wireframe ? GLEnum.Line : GLEnum.Fill);

            _sceneShader.Use();
            var viewProj = camera.ViewProjectionMatrix;
            _gl.UniformMatrix4(_uViewProj, 1, false, (float*)&viewProj);
            _gl.Uniform3(_uLightDir, LightDirection.X, LightDirection.Y, LightDirection.Z);
            _gl.Uniform3(_uFogColor, FogColor.X, FogColor.Y, FogColor.Z);
            _gl.Uniform2(_uFogRange, FogRange.X, FogRange.Y);
            _gl.Uniform3(_uCameraPos, camera.Position.X, camera.Position.Y, camera.Position.Z);
            _gl.Uniform1(_uTexture, 0);

            var visible = _level.VisibleInstances;
            _drawOrder.Clear();
            _drawOrder.AddRange(visible);
            _drawOrder.Sort(static (a, b) =>
            {
                var ga = a.IsLod ? 1 : 0; var gb = b.IsLod ? 1 : 0;
                if (ga != gb) return ga - gb;
                return string.CompareOrdinal(a.Definition.DffFile, b.Definition.DffFile);
            });

            GpuModel? boundModel = null;
            foreach (var inst in _drawOrder)
            {
                var gpu = _resources.GetModel(inst.Definition.Model);
                if (gpu == null) continue;

                if (!ReferenceEquals(gpu, boundModel))
                    boundModel = gpu;

                var world = inst.WorldMatrix;
                _gl.UniformMatrix4(_uModel, 1, false, (float*)&world);
                _gl.Uniform1(_uOpacity, DitherFades ? inst.Opacity : 1f);
                _gl.Uniform1(_uDitherInvert, inst.DitherInverted ? 1 : 0);

                foreach (var sub in gpu.SubMeshes)
                {
                    _gl.BindVertexArray(sub.Vao);
                    if (sub.Texture != 0)
                    {
                        _gl.BindTextureUnit(0, sub.Texture);
                        _gl.Uniform1(_uHasTexture, 1);
                    }
                    else
                    {
                        _gl.Uniform1(_uHasTexture, 0);
                    }
                    _gl.Uniform4(_uDiffuse, sub.DiffuseColor.X, sub.DiffuseColor.Y, sub.DiffuseColor.Z, sub.DiffuseColor.W);
                    _gl.DrawElements(PrimitiveType.Triangles, sub.IndexCount, DrawElementsType.UnsignedInt, (void*)0);
                    DrawCallsLastFrame++;
                    TrianglesLastFrame += (int)(sub.IndexCount / 3);
                }
            }

            _gl.BindVertexArray(0);
            _gl.PolygonMode(GLEnum.FrontAndBack, GLEnum.Fill);
        }

        private readonly List<WorldInstance> _drawOrder = new();

        public void Present(int windowWidth, int windowHeight)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            _gl.Viewport(0, 0, (uint)windowWidth, (uint)windowHeight);
            _gl.ClearColor(0, 0, 0, 1);
            _gl.Clear(ClearBufferMask.ColorBufferBit);

            if (UseBlitPresent)
            {
                var (dx0, dy0, dx1, dy1) = DestinationRect(windowWidth, windowHeight);
                _gl.BlitNamedFramebuffer(_replacementSwapchainFramebuffer, 0,
                                        0, 0, (int)InternalWidth, (int)InternalHeight,
                                        dx0, dy0, dx1, dy1,
                                        ClearBufferMask.ColorBufferBit,
                                        Filter == UpscaleFilter.Nearest ? GLEnum.Nearest : GLEnum.Linear);
                return;
            }

            _gl.Disable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.Blend);
            _upscaleShader.Use();
            _gl.BindTextureUnit(0, _swapchainColorTexture);
            _gl.Uniform1(_uSource, 0);
            _gl.Uniform2(_uSourceSize, (float)InternalWidth, (float)InternalHeight);
            _gl.Uniform1(_uFilter, (int)Filter);

            var (sx, sy) = PreserveAspectRatio ? FitScale(windowWidth, windowHeight) : (1f, 1f);
            _gl.Uniform4(_uScaleOffset, sx, sy, 0f, 0f);
            _gl.Uniform2(_uOutputSize, windowWidth * sx, windowHeight * sy);

            _gl.BindVertexArray(_emptyVao);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
            _gl.BindVertexArray(0);
        }

        private static (float sx, float sy) FitScale(int w, int h)
        {
            var windowAspect = (float)w / h;
            return windowAspect > InternalAspect
                ? (InternalAspect / windowAspect, 1f)
                : (1f, windowAspect / InternalAspect);
        }

        private (int x0, int y0, int x1, int y1) DestinationRect(int w, int h)
        {
            if (!PreserveAspectRatio) return (0, 0, w, h);
            var (sx, sy) = FitScale(w, h);
            var dw = (int)MathF.Round(w * sx);
            var dh = (int)MathF.Round(h * sy);
            var x0 = (w - dw) / 2;
            var y0 = (h - dh) / 2;
            return (x0, y0, x0 + dw, y0 + dh);
        }

        public void Dispose()
        {
            _resources.Dispose();
            _entities.Dispose();
            _sceneShader.Dispose();
            _upscaleShader.Dispose();
            _gl.DeleteVertexArray(_emptyVao);
            _gl.DeleteFramebuffer(_replacementSwapchainFramebuffer);
            _gl.DeleteTexture(_swapchainColorTexture);
            _gl.DeleteRenderbuffer(_swapchainDepthRenderbuffer);
        }
    }

    internal enum UpscaleFilter
    {
        Nearest = 0,
        Linear = 1,
        SharpBilinear = 2,
    }
}
