# NativeMessenger

This is system-wide native messenger for Unity Entities.
It provides an efficient way to trigger systems on demand
without involving entities data or any structural changes.

## Sending Messages

You can send values to messenger out of anywhere:
burst, jobs or managed context.

Below is example of system that sends test messages every 4th update.

```csharp
public partial struct SendTestSystem : ISystem
{
    private int _counter;

    public void OnUpdate(ref SystemState state)
    {
        if (_counter < 3)
        {
            _counter++;
        }
        else
        {
            _counter = 0;

            var messenger = SystemAPI.GetSingleton<Messenger>();
            messenger.Send(new TestMessage { value = 4.20f });
            messenger.Send(new TestMessage { value = 6.9f });
        }
    }
}
```

## Receiving Messages

You can use both: SystemBase and ISystem based systems to react to messages:

### Inheriting EventSystem

Below you can see an example of class based system which reacts to messages
in different ways:

* If you certain that message is only one, you can use `Message` property
* If multiple messages of same type can be sent you can read all of them via
  `Messages` property.

```csharp
public partial class TestClassSystem : EventSystem<TestMessage>
{
    protected override void OnUpdate()
    {
        Debug.Log(Message.value);
        foreach (var testMessage in Messages)
        {
            Debug.Log(testMessage.value);
        }
    }
}
```

### Inheriting IEventSystem

Below you can see an example of ISystem based system.

```csharp
[UpdateInGroup(typeof(NativeEventSystemGroup))]
public partial struct TestSystem : ISystem, IEventSystem<TestMessage>
{
    [Message] private NativeArray<TestMessage> _message;

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var testMessage in _message)
        {
            Debug.Log($"value: {testMessage.value}");
        }
    }
}
```

Unlike class based, ISystem requires `[UpdateInGroup(typeof(NativeEventSystemGroup))]`
attribute in order to receive messages in proper system.

And in order to read messages it needs
special attribute `Message` on a field.
This field can only be one per system and can be used only one two types:

* Either `TestMessage` type directly

```csharp
[Message] private TestMessage _message;
```

* Or `NativeArray<TestMessage>` if you need multiple

```csharp
[Message] private NativeArray<TestMessage> _message;
```

* Or you may not use field at all if you only want to trigger `OnUpdate`
  without reading any data.

