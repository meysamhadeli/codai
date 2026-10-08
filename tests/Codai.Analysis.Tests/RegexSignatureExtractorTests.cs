using Codai.Analysis.Extractors;
using FluentAssertions;

namespace Codai.Analysis.Tests;

public class RegexSignatureExtractorTests
{
    [Fact]
    public void Extracts_go_signatures()
    {
        const string source = """
            package main

            type Repository interface {
            }

            type Order struct {
            }

            func main() {
            }

            func (s *Service) Handle() {
            }
            """;

        var elements = SignatureRules.Go().Extract("main.go", source);

        elements.Should().Contain("package: main");
        elements.Should().Contain("interface: Repository");
        elements.Should().Contain("struct: Order");
        elements.Should().Contain("function: main");
        elements.Should().Contain("method: Handle");
    }

    [Fact]
    public void Extracts_python_signatures()
    {
        const string source = """
            @dataclass
            class Order:
                def submit(self):
                    pass
            """;

        var elements = SignatureRules.Python().Extract("order.py", source);

        elements.Should().Contain("decorated: dataclass");
        elements.Should().Contain("class: Order");
        elements.Should().Contain("function: submit");
    }

    [Fact]
    public void Extracts_java_signatures()
    {
        const string source = """
            package com.acme.orders;

            public interface IOrder {}

            public enum Status { NEW }

            public class OrderService {
                public void submit() {}
            }
            """;

        var elements = SignatureRules.Java().Extract("OrderService.java", source);

        elements.Should().Contain("package: com.acme.orders");
        elements.Should().Contain("interface: IOrder");
        elements.Should().Contain("enum: Status");
        elements.Should().Contain("class: OrderService");
        elements.Should().Contain("method: submit");
    }

    [Fact]
    public void Extracts_javascript_signatures()
    {
        const string source = """
            class Order {}

            function submit() {}

            const handler = () => {};
            """;

        var elements = SignatureRules.JavaScript().Extract("order.js", source);

        elements.Should().Contain("class: Order");
        elements.Should().Contain("function: submit");
        elements.Should().Contain("anonymous_function: handler");
    }

    [Fact]
    public void Extracts_typescript_signatures()
    {
        const string source = """
            class Order {}

            interface IOrder {}

            enum Status {}

            function submit() {}

            const handler = () => {};
            """;

        var elements = SignatureRules.TypeScript().Extract("order.ts", source);

        elements.Should().Contain("class: Order");
        elements.Should().Contain("interface: IOrder");
        elements.Should().Contain("enum: Status");
        elements.Should().Contain("function: submit");
        elements.Should().Contain("anonymous_function: handler");
    }
}

public class SignatureExtractorRegistryTests
{
    private readonly SignatureExtractorRegistry _registry = new();

    [Theory]
    [InlineData("Program.cs", true)]
    [InlineData("main.go", true)]
    [InlineData("app.ts", true)]
    [InlineData("app.js", true)]
    [InlineData("main.py", true)]
    [InlineData("Main.java", true)]
    [InlineData("README.md", false)]
    public void Reports_supported_files(string path, bool expected)
    {
        _registry.Supports(path).Should().Be(expected);
    }

    [Fact]
    public void Routes_csharp_to_roslyn_extractor()
    {
        var elements = _registry.Extract("Order.cs", "namespace A; public class B {}");

        elements.Should().Contain("class: B");
    }

    [Fact]
    public void Returns_empty_for_unsupported_files()
    {
        _registry.Extract("data.json", "{}").Should().BeEmpty();
    }
}
