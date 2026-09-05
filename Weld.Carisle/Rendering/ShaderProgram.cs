using Silk.NET.OpenGL;

namespace Weld.Carisle.Rendering;

internal sealed class ShaderProgram : IDisposable
{
    private readonly GL _gl;
    public uint Handle { get; }

    public ShaderProgram(GL gl, string vertexSource, string fragmentSource)
    {
        _gl = gl;
        var vs = Compile(ShaderType.VertexShader, vertexSource);
        var fs = Compile(ShaderType.FragmentShader, fragmentSource);

        Handle = _gl.CreateProgram();
        _gl.AttachShader(Handle, vs);
        _gl.AttachShader(Handle, fs);
        _gl.LinkProgram(Handle);
        _gl.GetProgram(Handle, ProgramPropertyARB.LinkStatus, out var ok);
        if (ok == 0)
            throw new Exception("Program link failed: " + _gl.GetProgramInfoLog(Handle));

        _gl.DetachShader(Handle, vs);
        _gl.DetachShader(Handle, fs);
        _gl.DeleteShader(vs);
        _gl.DeleteShader(fs);
    }

    private uint Compile(ShaderType type, string source)
    {
        var shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        _gl.GetShader(shader, ShaderParameterName.CompileStatus, out var ok);
        if (ok == 0)
            throw new Exception($"{type} compile failed: " + _gl.GetShaderInfoLog(shader));
        return shader;
    }

    public void Use() => _gl.UseProgram(Handle);

    public int Uniform(string name)
    {
        var loc = _gl.GetUniformLocation(Handle, name);
        if (loc < 0) Console.WriteLine($"[RenderPipeline] warning: uniform '{name}' not found (optimised out?)");
        return loc;
    }

    public void Dispose() => _gl.DeleteProgram(Handle);
}
