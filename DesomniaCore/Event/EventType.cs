using System.Reflection;

namespace MadWizard.Desomnia
{
    public class EventType(EventInfo type, FieldInfo field)
    {
        public string Name => type.Name;

        public void AddEventHandler(EventSource source, EventInvocation handler)
        {
            type.GetAddMethod(true)?.Invoke(source, [handler]);

            //type.AddEventHandler(source, handler);
        }

        public bool HasAnyEventHandler(EventSource source)
        {
            return field.GetValue(source) is Delegate;
        }

        public void RemoveEventHandler(EventSource source, EventInvocation handler)
        {
            type.GetRemoveMethod(true)?.Invoke(source, [handler]);

            //type.RemoveEventHandler(source, handler);
        }

        public Delegate[] GetInvocationList(EventSource source)
        {
            return field.GetValue(source) is Delegate gate ? gate.GetInvocationList() : [];
        }

    }
}
