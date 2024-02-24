using Microsoft.Extensions.DependencyInjection;
using Remora.Commands.DependencyInjection;
using Remora.Commands.Groups;
using Remora.Commands.Trees;
using SerenaBot.Commands.Util;
using System.Reflection;

namespace SerenaBot.Extensions;

public static class TreeRegistrationBuilderExtensions
{
    private static readonly FieldInfo NameField = typeof(TreeRegistrationBuilder).GetField("_treeName", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo ServicesField = typeof(TreeRegistrationBuilder).GetField("_serviceCollection", BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static TreeRegistrationBuilder WithCommandGroup<TCommandGroup>(this TreeRegistrationBuilder tree, bool injectUsingReflection) where TCommandGroup : CommandGroup
    {
        if (!injectUsingReflection)
        {
            return tree.WithCommandGroup(typeof(TCommandGroup));
        }

        IServiceCollection services = ServicesField.GetValue(tree) as IServiceCollection ?? throw new NullReferenceException();
        string? treeName = NameField.GetValue(tree) as string;

        services.Configure<CommandTreeBuilder>
        (
            treeName,
            builder => builder.RegisterModule<TCommandGroup>()
        );

        services.Configure<CommandTreeBuilder>
        (
            Remora.Commands.Constants.AllTreeName,
            builder => builder.RegisterModule<TCommandGroup>()
        );

        static void AddCommandGroup(IServiceCollection services, Type type)
        {
            if (!type.IsSubclassOf(typeof(CommandGroup)))
            {
                throw new ArgumentException($"Given type must be a subclass of {nameof(CommandGroup)}");
            }

            services.AddScoped(type, s =>
            {
                object commandService;
                try
                {
                    commandService = Activator.CreateInstance(type) ?? throw new NullReferenceException($"Activator.CreateInstance returned null for type '{type.Name}'");
                }
                catch (MissingMethodException)
                {
                    throw new InvalidOperationException($"Type '{type.Name}' must contain a parameterless constructor for reflected injection to function");
                }

                // Changing the outer 'type' local breaks subsequent calls to this service
                Type iterType = type;
                while (iterType.IsSubclassOf(typeof(CommandGroup)))
                {
                    foreach (FieldInfo field in iterType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                    {
                        object? service = s.GetService(field.FieldType) ?? s.CreateScope().ServiceProvider.GetService(field.FieldType);
                        field.SetValue(commandService, service);
                    }

                    if (iterType.BaseType == null)
                    {
                        break;
                    }

                    iterType = iterType.BaseType;
                }

                if (commandService is IInitializer init)
                {
                    init.Initialize();
                }

                return commandService;
            });

            foreach (Type nested in type.GetNestedTypes().Where(t => t.IsSubclassOf(typeof(CommandGroup)) && t != typeof(CommandGroup) && t != typeof(BaseCommandGroup)))
            {
                AddCommandGroup(services, nested);
            }
        }

        AddCommandGroup(services, typeof(TCommandGroup));
        return tree;
    }
}
