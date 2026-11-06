using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;
namespace DeepSpaceAE.Tests;
public sealed class GamePatchAuditTests
{
    private abstract class BodyFixture
    {
        public abstract void WithoutBody();
        public void WithBody() { GC.KeepAlive(this); }
    }
    [Fact]
    public void AbstractMethodsAreNotPatchableEvenWhenTheirDeclarationsExist()
    {
        using var input=File.OpenRead(typeof(GamePatchAuditTests).Assembly.Location);
        using var pe=new PEReader(input);var reader=pe.GetMetadataReader();
        var type=reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t=>reader.GetString(t.Name)=="BodyFixture");
        var methods=type.GetMethods().Select(reader.GetMethodDefinition).ToDictionary(m=>reader.GetString(m.Name));
        Assert.False(GamePatchAudit.HasPatchableBody(methods["WithoutBody"]));
        Assert.True(GamePatchAudit.HasPatchableBody(methods["WithBody"]));
    }
}
