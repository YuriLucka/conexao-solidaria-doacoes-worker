using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConexaoSolidaria.Messaging;

public static class MassTransitExtensions
{
    // Lê RabbitMq:Host/User/Pass (ConfigMap + Secret) e RabbitMq:Port (opcional, padrão 5672); falha se faltar.
    // Registra retry padrão.
    // MassTransit fica na 8.x: a 9.x tem licença comercial.
    public static IServiceCollection AddConexaoMessaging(
        this IServiceCollection services,
        IConfiguration cfg,
        Action<IBusRegistrationConfigurator>? configure = null)
    {
        services.AddMassTransit(x =>
        {
            x.SetKebabCaseEndpointNameFormatter();
            configure?.Invoke(x);

            x.UsingRabbitMq((ctx, bus) =>
            {
                var porta = ushort.TryParse(cfg["RabbitMq:Port"], out var p) ? p : (ushort)5672;
                bus.Host(Required(cfg, "RabbitMq:Host"), porta, "/", h =>
                {
                    h.Username(Required(cfg, "RabbitMq:User"));
                    h.Password(Required(cfg, "RabbitMq:Pass"));
                });
                bus.UseMessageRetry(r => r.Interval(3, TimeSpan.FromSeconds(5)));
                bus.ConfigureEndpoints(ctx);
            });
        });
        return services;
    }

    private static string Required(IConfiguration cfg, string key)
    {
        var value = cfg[key];
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Configuração '{key}' não informada.");
        return value;
    }
}
