using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using Newtonsoft.Json;

namespace SnakeGame;

public class ControlCommand
{
	public string moving;

	public ControlCommand(string m)
	{
		moving = m;
	}

	public override string ToString()
	{
		return JsonConvert.SerializeObject((object)this);
	}
}
