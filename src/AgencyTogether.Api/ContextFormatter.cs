using System.Text;

namespace AgencyTogether.Api;

public static class ContextFormatter
{
    public static string Render(
        string room,
        long cursor,
        IReadOnlyList<AgentPresence> agents,
        IReadOnlyList<ChatMessage> messages)
    {
        var brief = new StringBuilder();

        brief.Append("# Agency room: ").AppendLine(room);
        brief.Append("Cursor: ").Append(cursor).AppendLine();
        brief.AppendLine();

        brief.AppendLine("## Agents");
        if (agents.Count == 0)
        {
            brief.AppendLine("_No agents have posted yet._");
        }
        else
        {
            foreach (var agent in agents)
            {
                brief.Append("- **").Append(agent.Name).Append("** (`").Append(agent.AgentId)
                     .Append("`) - goal: ")
                     .AppendLine(string.IsNullOrWhiteSpace(agent.Goal) ? "_none stated_" : agent.Goal);
            }
        }

        brief.AppendLine();
        brief.AppendLine("## Transcript");
        if (messages.Count == 0)
        {
            brief.AppendLine("_No messages yet._");
            return brief.ToString();
        }

        foreach (var message in messages)
        {
            brief.Append('[').Append(message.Seq).Append("] ")
                 .Append(message.Name).Append(" (`").Append(message.AgentId).Append("`) ")
                 .AppendLine(message.Timestamp.UtcDateTime.ToString("O"));
            brief.AppendLine(message.Content);
            brief.AppendLine();
        }

        return brief.ToString();
    }
}
