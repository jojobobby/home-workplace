using HomeWorkplace.Client;
using HomeWorkplace.Office.Sim;

namespace HomeWorkplace.Office.Tests;

public class SimulationTests
{
    private const float Dt = 1f / 60f;

    private static Simulation NewSim(params string[] ids) => new(WorldLayout.Generate(ids), seed: 7);

    /// <summary>Step the simulation until the condition holds or the time budget runs out.</summary>
    private static bool AdvanceUntil(Simulation sim, Func<bool> done, float maxSeconds)
    {
        var steps = (int)(maxSeconds / Dt);
        for (var i = 0; i < steps; i++)
        {
            if (done()) return true;
            sim.Update(Dt);
        }
        return done();
    }

    private static Agent Seat(Simulation sim, string id, EmployeeStatus status = EmployeeStatus.Awake)
    {
        sim.Apply(new EmployeeAppeared(id, id, status, null));
        Assert.True(AdvanceUntil(sim, () => sim.Agents[id].Activity == Activity.IdleAtDesk || sim.Agents[id].Activity == Activity.Typing, 30f),
            $"{id} never reached its desk");
        return sim.Agents[id];
    }

    [Fact]
    public void An_appearing_awake_employee_walks_from_the_door_to_its_desk()
    {
        var sim = NewSim("ada");
        sim.Apply(new EmployeeAppeared("ada", "Ada", EmployeeStatus.Awake, null));

        var ada = sim.Agents["ada"];
        Assert.Equal(sim.World.Spawn, ada.Tile);
        Assert.Equal(Anim.Walk, ada.Anim);

        Assert.True(AdvanceUntil(sim, () => ada.Activity == Activity.IdleAtDesk, 30f));
        Assert.Equal(sim.World.DeskOf("ada")!.Seat, ada.Tile);
        Assert.Equal(Anim.Idle, ada.Anim);
    }

    [Fact]
    public void A_working_employee_types_at_its_desk_and_throws_sparks()
    {
        var sim = NewSim("ada");
        Seat(sim, "ada");

        sim.Apply(new EmployeeStatusChanged("ada", EmployeeStatus.Working, "Build the parser", null));

        Assert.Equal(Activity.Typing, sim.Agents["ada"].Activity);
        Assert.Equal(Anim.Type, sim.Agents["ada"].Anim);
        Assert.Equal("Build the parser", sim.Agents["ada"].TaskTitle);
        Assert.True(AdvanceUntil(sim, () => sim.Moments.Any(m => m.Kind == MomentKind.Particles && m.Detail == "sparks"), 5f));
    }

    [Fact]
    public void An_asleep_employee_walks_home_and_disappears_with_a_door_sound()
    {
        var sim = NewSim("ada");
        Seat(sim, "ada");

        sim.Apply(new EmployeeStatusChanged("ada", EmployeeStatus.Asleep, null, null));

        Assert.Equal(Activity.WalkingHome, sim.Agents["ada"].Activity);
        Assert.True(AdvanceUntil(sim, () => sim.Agents["ada"].Activity == Activity.Absent, 30f));
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Sound && m.Detail == "door");
    }

    [Fact]
    public void Awake_employees_wander_to_coffee_and_back_on_a_seeded_timer()
    {
        var sim = NewSim("ada");
        Seat(sim, "ada");

        var sawCoffee = AdvanceUntil(sim, () => sim.Agents["ada"].Activity == Activity.AtCoffee, 90f);
        Assert.True(sawCoffee, "never went for coffee");
        Assert.Equal(sim.World.CoffeeSpot, sim.Agents["ada"].Tile);
        Assert.True(AdvanceUntil(sim, () => sim.Moments.Any(m => m.Kind == MomentKind.Particles && m.Detail == "steam"), 5f));
        Assert.True(AdvanceUntil(sim, () => sim.Agents["ada"].Activity == Activity.IdleAtDesk, 30f), "never came back");
    }

    [Fact]
    public void Waiting_walks_to_the_teammate_and_talks()
    {
        var sim = NewSim("ada", "rex");
        Seat(sim, "ada");
        Seat(sim, "rex");

        sim.Apply(new EmployeeStatusChanged("ada", EmployeeStatus.Waiting, "Parser", WaitingOn: "rex"));

        Assert.True(AdvanceUntil(sim, () => sim.Agents["ada"].Activity == Activity.Talking, 30f));
        var rexSeat = sim.World.DeskOf("rex")!.Seat;
        Assert.Equal(1, sim.Agents["ada"].Tile.ManhattanTo(rexSeat));
        Assert.Equal(Anim.Talk, sim.Agents["ada"].Anim);
        Assert.Equal(BubbleKind.Dots, sim.Agents["ada"].Bubble?.Kind);
    }

    [Fact]
    public void Events_raise_the_right_moments()
    {
        var sim = NewSim("ada", "rex");
        Seat(sim, "ada");
        Seat(sim, "rex");

        sim.Apply(new HandoffAnswered("ada"));
        Assert.Equal(BubbleKind.Exclaim, sim.Agents["ada"].Bubble?.Kind);
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Sound && m.Detail == "chime");

        sim.Apply(new RunFinished("rex", Succeeded: true));
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Particles && m.Detail == "sparkle" && m.At == sim.World.DeskOf("rex")!.Pos);
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Sound && m.Detail == "ding");

        sim.Apply(new RunFinished("rex", Succeeded: false));
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Shake);
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Particles && m.Detail == "smoke");
        Assert.Contains(sim.Moments, m => m.Kind == MomentKind.Sound && m.Detail == "buzz");

        sim.Apply(new HumanNeeded("ada"));
        Assert.Equal(BubbleKind.Exclaim, sim.Agents["ada"].Bubble?.Kind);
        Assert.True(sim.Agents["ada"].Bubble!.Persistent);
    }

    [Fact]
    public void A_handoff_request_sends_the_asker_over_with_a_question_and_back()
    {
        var sim = NewSim("ada", "rex");
        Seat(sim, "ada");
        Seat(sim, "rex");

        sim.Apply(new HandoffRequested("ada", "rex"));

        Assert.True(AdvanceUntil(sim, () => sim.Agents["ada"].Activity == Activity.Talking, 30f));
        Assert.Equal(BubbleKind.Question, sim.Agents["ada"].Bubble?.Kind);
        Assert.True(AdvanceUntil(sim, () => sim.Agents["ada"].Activity == Activity.IdleAtDesk, 40f), "never returned");
    }

    [Fact]
    public void Moments_expire_and_walking_agents_make_footsteps_and_face_their_direction()
    {
        var sim = NewSim("ada");
        sim.Apply(new RunFinished("ada", Succeeded: true));   // absent agent: still a sound, at the desk
        var ding = sim.Moments.Single(m => m.Detail == "ding");
        Assert.True(AdvanceUntil(sim, () => !sim.Moments.Contains(ding), ding.Ttl + 1f));

        sim.Apply(new EmployeeAppeared("ada", "Ada", EmployeeStatus.Awake, null));
        Assert.True(AdvanceUntil(sim, () => sim.Moments.Any(m => m.Kind == MomentKind.Sound && m.Detail == "footstep"), 3f));
        Assert.False(sim.Agents["ada"].FacingLeft);   // the door is bottom-left; the desk is to the right
    }
}

public class TicketClaimTests
{
    private static Simulation Seated()
    {
        var sim = new Simulation(WorldLayout.Generate(new[] { "ada-coder" }), seed: 3);
        sim.Apply(new EmployeeAppeared("ada-coder", "Ada", EmployeeStatus.Working, "Fix the parser"));
        for (var i = 0; i < 60 * 40; i++) sim.Update(1f / 60f);
        Assert.Equal(Activity.Typing, sim.Agents["ada-coder"].Activity);
        return sim;
    }

    [Fact]
    public void A_claim_sends_the_employee_to_the_board_and_back_to_typing()
    {
        var sim = Seated();
        var ada = sim.Agents["ada-coder"];

        sim.Apply(new TicketClaimed("ada-coder"));
        Assert.Equal(Activity.WalkingToBoard, ada.Activity);

        var atBoard = false; var page = false; var typing = false;
        for (var i = 0; i < 60 * 40 && !typing; i++)
        {
            sim.Update(1f / 60f);
            if (ada.Activity == Activity.AtBoard)
            {
                atBoard = true;
                Assert.Equal(sim.World.TicketSpot, ada.Tile);
                Assert.Equal(BubbleKind.Exclaim, ada.Bubble?.Kind);
            }
            if (sim.Moments.Any(m => m.Kind == MomentKind.Sound && m.Detail == "page")) page = true;
            typing = atBoard && ada.Activity == Activity.Typing;
        }
        Assert.True(atBoard, "visited the board");
        Assert.True(page, "took the ticket with a page sound");
        Assert.True(typing, "went back to typing");
        Assert.False(ada.OnErrand);
    }

    [Fact]
    public void A_status_change_during_the_errand_waits_until_the_errand_is_done()
    {
        var sim = Seated();
        var ada = sim.Agents["ada-coder"];
        sim.Apply(new TicketClaimed("ada-coder"));
        sim.Apply(new EmployeeStatusChanged("ada-coder", EmployeeStatus.Working, "Fix the parser", null));
        Assert.Equal(Activity.WalkingToBoard, ada.Activity);   // not re-aimed at the desk
    }

    [Fact]
    public void The_open_ticket_count_is_kept_for_the_board_sprite()
    {
        var sim = Seated();
        Assert.Equal(0, sim.OpenTickets);
        sim.Apply(new TicketsChanged(3));
        Assert.Equal(3, sim.OpenTickets);
    }
}
