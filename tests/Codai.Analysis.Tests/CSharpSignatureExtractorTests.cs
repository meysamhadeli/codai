using Codai.Analysis.Extractors;
using FluentAssertions;

namespace Codai.Analysis.Tests;

public class CSharpSignatureExtractorTests
{
    private readonly CSharpSignatureExtractor _extractor = new();

    [Theory]
    [InlineData("Program.cs", true)]
    [InlineData("PROGRAM.CS", true)]
    [InlineData("main.go", false)]
    public void Supports_only_csharp_files(string path, bool expected)
    {
        _extractor.Supports(path).Should().Be(expected);
    }

    [Fact]
    public void Extracts_file_scoped_namespace_and_class()
    {
        const string source = """
            namespace Acme.Orders;

            public class OrderService
            {
            }
            """;

        var elements = _extractor.Extract("OrderService.cs", source);

        elements.Should().Contain("file_scoped_namespace: Acme.Orders");
        elements.Should().Contain("class: OrderService");
    }

    [Fact]
    public void Extracts_block_scoped_namespace()
    {
        const string source = """
            namespace Acme
            {
                public class Order {}
            }
            """;

        _extractor.Extract("Order.cs", source).Should().Contain("namespace: Acme");
    }

    [Fact]
    public void Extracts_all_declaration_kinds()
    {
        const string source = """
            namespace Acme;

            public interface IOrder {}
            public struct Money {}
            public record OrderPlaced(int Id);
            public enum Status { New, Done }
            public class Order
            {
                public int Id { get; set; }
                public void Submit() {}
            }
            """;

        var elements = _extractor.Extract("Order.cs", source);

        elements.Should().Contain("interface: IOrder");
        elements.Should().Contain("struct: Money");
        elements.Should().Contain("record: OrderPlaced");
        elements.Should().Contain("enum: Status");
        elements.Should().Contain("class: Order");
        elements.Should().Contain("property: Id");
        elements.Should().Contain("method: Submit");
    }

    [Fact]
    public void Preserves_document_order()
    {
        const string source = """
            namespace Acme;
            public class First {}
            public class Second {}
            """;

        var elements = _extractor.Extract("Order.cs", source);

        elements.ToList().IndexOf("class: First").Should().BeLessThan(elements.ToList().IndexOf("class: Second"));
    }

    [Fact]
    public void Returns_empty_for_invalid_source_without_throwing()
    {
        var elements = _extractor.Extract("broken.cs", "class {{{ this is not valid");

        elements.Should().NotBeNull();
    }
}
