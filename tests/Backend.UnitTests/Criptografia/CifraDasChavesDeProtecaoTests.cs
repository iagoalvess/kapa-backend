using System.Xml.Linq;
using Backend.Data.Criptografia;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Backend.UnitTests.Criptografia;

public sealed class CifraDasChavesDeProtecaoTests
{
    private const string ChaveDeDados = "Ur+vVk2HCLImlGsrSpOBlupA/+iuadjrj2bPBC0MSgI=";

    [Fact]
    public void Chave_gravada_vai_cifrada_e_outro_processo_le_o_que_foi_protegido()
    {
        // Arrange
        var repositorio = new ChavesEmMemoria();
        var protegido = Protetor(repositorio).Protect("redefinir-senha");

        // Act
        var lido = Protetor(repositorio).Unprotect(protegido);

        // Assert
        lido.ShouldBe("redefinir-senha");
        var gravado = repositorio.GetAllElements().ShouldHaveSingleItem().ToString();
        gravado.ShouldNotContain("<masterKey");
        gravado.ShouldContain(nameof(CifraDasChavesDeProtecao));
    }

    /// <summary>Um contêiner novo por chamada: o segundo só acha a chave lendo — e decifrando — o repositório.</summary>
    private static IDataProtector Protetor(IXmlRepository repositorio)
    {
        var servicos = new ServiceCollection();
        servicos.AddSingleton(new CifraDeCampo(Options.Create(new CriptografiaSettings { ChaveDeDados = ChaveDeDados })));
        servicos.AddDataProtection().SetApplicationName("kapa");
        servicos
            .AddOptions<KeyManagementOptions>()
            .Configure<CifraDeCampo>(
                (opcoes, cifra) =>
                {
                    opcoes.XmlRepository = repositorio;
                    opcoes.XmlEncryptor = new CifraDasChavesDeProtecao(cifra);
                }
            );

        return servicos.BuildServiceProvider().GetDataProtector("teste");
    }

    private sealed class ChavesEmMemoria : IXmlRepository
    {
        private readonly List<XElement> _elementos = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _elementos.ToList();

        public void StoreElement(XElement element, string friendlyName) => _elementos.Add(element);
    }
}
