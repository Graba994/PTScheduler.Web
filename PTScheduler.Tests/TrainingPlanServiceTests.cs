using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PTScheduler.Application.DTOs;
using PTScheduler.Domain.Entities;
using PTScheduler.Domain.Enums;
using PTScheduler.Infrastructure.Data;
using PTScheduler.Infrastructure.Services;
using PTScheduler.Tests.Helpers;
using Xunit;

namespace PTScheduler.Tests;

/// <summary>
/// Kreator planów: zapis grafu plan→dni→ćwiczenia z dopasowaniem po Id
/// (dodanie/edycja/usunięcie), szablon vs przypisanie klienta, duplikacja,
/// usuwanie i pomijanie niewidocznych ćwiczeń.
/// </summary>
public class TrainingPlanServiceTests
{
    private const string T1 = "trainer-1";
    private const string T2 = "trainer-2";

    private static TrainingPlanService Make(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
        => new(f, TestClock.AtWallClock(new DateTime(2026, 1, 1, 12, 0, 0)));

    private static async Task SeedAsync(Microsoft.EntityFrameworkCore.IDbContextFactory<ApplicationDbContext> f)
    {
        await using var db = f.CreateDbContext();
        db.Clients.Add(new Client { Id = 1, ApplicationUserId = "c1", FirstName = "Jan", LastName = "Kowalski", TrainerUserId = T1 });
        db.Exercises.AddRange(
            new Exercise { Id = 1, Visibility = ExerciseVisibility.Public, NamePl = "Przysiad", NameEn = "Squat", PrimaryMuscles = "quadriceps", ImageUrls = "https://x/s0.jpg" },
            new Exercise { Id = 2, Visibility = ExerciseVisibility.Public, NamePl = "Wyciskanie", NameEn = "Bench", PrimaryMuscles = "chest" },
            new Exercise { Id = 3, Visibility = ExerciseVisibility.Public, NamePl = "Martwy ciąg", NameEn = "Deadlift", PrimaryMuscles = "hamstrings" }
        );
        await db.SaveChangesAsync();
    }

    private static PlanEditDto NewPlan() => new()
    {
        Name = "Plan A",
        IsTemplate = false,
        ClientId = 1,
        Days =
        [
            new PlanDayEditDto
            {
                Label = "Dzień A",
                Exercises =
                [
                    new PlanExerciseEditDto { ExerciseId = 1, Sets = 3, Reps = "8-12", TargetWeightKg = 60m },
                    new PlanExerciseEditDto { ExerciseId = 2, Sets = 4, Reps = "6" }
                ]
            }
        ]
    };

    [Fact]
    public async Task Create_Then_Load_Roundtrips_Structure()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        var loaded = await svc.GetForEditAsync(T1, id);

        loaded.Should().NotBeNull();
        loaded!.ClientId.Should().Be(1);
        loaded.Days.Should().ContainSingle();
        loaded.Days[0].Label.Should().Be("Dzień A");
        loaded.Days[0].Exercises.Select(e => e.ExerciseId).Should().ContainInOrder(1, 2);
        loaded.Days[0].Exercises[0].Order.Should().Be(0);
        loaded.Days[0].Exercises[1].Order.Should().Be(1);
        loaded.Days[0].Exercises[0].ExerciseNamePl.Should().Be("Przysiad");
        loaded.Days[0].Exercises[0].ThumbnailUrl.Should().Be("https://x/s0.jpg");
    }

    [Fact]
    public async Task Update_Diffs_Days_And_Exercises_Keeping_Ids()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        var dto = await svc.GetForEditAsync(T1, id);
        var keptId = dto!.Days[0].Exercises[0].Id;

        // Usuń drugie ćwiczenie, dodaj nowe (id 3), dołóż drugi dzień.
        dto.Days[0].Exercises.RemoveAt(1);
        dto.Days[0].Exercises.Add(new PlanExerciseEditDto { ExerciseId = 3, Sets = 5 });
        dto.Days.Add(new PlanDayEditDto { Label = "Dzień B", Exercises = [new PlanExerciseEditDto { ExerciseId = 2, Sets = 3 }] });

        await svc.SavePlanAsync(T1, dto);
        var reloaded = await svc.GetForEditAsync(T1, id);

        reloaded!.Days.Should().HaveCount(2);
        reloaded.Days[0].Exercises.Select(e => e.ExerciseId).Should().ContainInOrder(1, 3);
        reloaded.Days[0].Exercises[0].Id.Should().Be(keptId); // istniejący wiersz zachowany
        reloaded.Days[1].Label.Should().Be("Dzień B");
    }

    [Fact]
    public async Task Template_Forces_Null_Client()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var dto = NewPlan();
        dto.IsTemplate = true;
        dto.ClientId = 1;
        var id = await svc.SavePlanAsync(T1, dto);

        var loaded = await svc.GetForEditAsync(T1, id);
        loaded!.IsTemplate.Should().BeTrue();
        loaded.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_Exercise_Is_Skipped()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var dto = NewPlan();
        dto.Days[0].Exercises.Add(new PlanExerciseEditDto { ExerciseId = 999, Sets = 3 }); // nie istnieje

        var id = await svc.SavePlanAsync(T1, dto);
        var loaded = await svc.GetForEditAsync(T1, id);
        loaded!.Days[0].Exercises.Select(e => e.ExerciseId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [Fact]
    public async Task Duplicate_Copies_Structure_Without_Client()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        var copyId = await svc.DuplicateAsync(T1, id, asTemplate: true);

        copyId.Should().NotBe(id);
        var copy = await svc.GetForEditAsync(T1, copyId);
        copy!.Name.Should().Contain("kopia");
        copy.IsTemplate.Should().BeTrue();
        copy.ClientId.Should().BeNull();
        copy.Days[0].Exercises.Select(e => e.ExerciseId).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task Delete_Removes_Plan()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());
        await svc.DeletePlanAsync(T1, id);
        (await svc.GetForEditAsync(T1, id)).Should().BeNull();
    }

    [Fact]
    public async Task GetPlans_Returns_Counts_And_Respects_Ownership()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var id = await svc.SavePlanAsync(T1, NewPlan());

        var mine = await svc.GetPlansAsync(T1);
        mine.Should().ContainSingle();
        mine[0].DayCount.Should().Be(1);
        mine[0].ExerciseCount.Should().Be(2);
        mine[0].ClientName.Should().Be("Jan Kowalski");

        (await svc.GetPlansAsync(T2)).Should().BeEmpty();
        (await svc.GetForEditAsync(T2, id)).Should().BeNull(); // nie widać cudzego
    }

    [Fact]
    public async Task Save_Requires_Name()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var dto = NewPlan();
        dto.Name = "  ";
        var act = async () => await svc.SavePlanAsync(T1, dto);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
    [Fact]
    public async Task New_Client_Plan_Lands_On_Top_And_Reorder_Persists()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);

        var a = await svc.SavePlanAsync(T1, NewPlan());
        var b2 = NewPlan(); b2.Name = "Plan B";
        var b = await svc.SavePlanAsync(T1, b2);

        (await svc.GetClientPlansAsync(1)).Select(p => p.Id).Should().ContainInOrder(b, a);

        await svc.ReorderClientPlansAsync(T1, false, 1, [a, b]);
        (await svc.GetClientPlansAsync(1)).Select(p => p.Id).Should().ContainInOrder(a, b);

        // Cudzy trener nie przestawi kolejności.
        await svc.ReorderClientPlansAsync(T2, false, 1, [b, a]);
        (await svc.GetClientPlansAsync(1)).Select(p => p.Id).Should().ContainInOrder(a, b);
    }

    [Fact]
    public async Task Archived_Plans_Are_Hidden_Unless_Requested()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);
        var id = await svc.SavePlanAsync(T1, NewPlan());

        await svc.SetArchivedAsync(T1, false, id, true);
        (await svc.GetClientPlansAsync(1)).Should().BeEmpty();
        (await svc.GetClientPlansAsync(1, includeArchived: true)).Should().ContainSingle(p => p.Id == id && p.IsArchived);

        await svc.SetArchivedAsync(T1, false, id, false);
        (await svc.GetClientPlansAsync(1)).Should().ContainSingle();
    }

    [Fact]
    public async Task Use_Mode_Roundtrips()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);
        var dto = NewPlan();
        dto.Use = PlanUse.Solo;

        var id = await svc.SavePlanAsync(T1, dto);

        (await svc.GetForEditAsync(T1, id))!.Use.Should().Be(PlanUse.Solo);
        (await svc.GetClientPlansAsync(1)).Single().Use.Should().Be(PlanUse.Solo);
    }

    [Fact]
    public async Task Assign_Session_Day_Only_From_Same_Client()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);
        var planId = await svc.SavePlanAsync(T1, NewPlan());
        int dayId, foreignDayId;
        await using (var db = f.CreateDbContext())
        {
            db.Clients.Add(new Client { Id = 2, ApplicationUserId = "c2", FirstName = "Ola", LastName = "Nowak", TrainerUserId = T1 });
            db.TrainingPlans.Add(new TrainingPlan { Id = 99, Name = "Cudzy", ClientId = 2, TrainerUserId = T1, Days = [new PlanDay { Id = 990, Label = "X" }] });
            db.Sessions.Add(new Session { Id = 5, ClientId = 1, SessionTypeId = 1, TrainerUserId = T1, StartTime = new DateTime(2026, 1, 2, 10, 0, 0) });
            await db.SaveChangesAsync();
            dayId = db.PlanDays.Single(d => d.PlanId == planId).Id;
            foreignDayId = 990;
        }

        (await svc.AssignSessionDayAsync(T1, false, 5, foreignDayId)).Should().BeFalse();
        (await svc.AssignSessionDayAsync(T2, false, 5, dayId)).Should().BeFalse();
        (await svc.AssignSessionDayAsync(T1, false, 5, dayId)).Should().BeTrue();

        var map = await svc.GetSessionPlanDaysAsync([5]);
        map[5].PlanDayId.Should().Be(dayId);
        map[5].DayLabel.Should().Be("Dzień A");
        map[5].ExerciseCount.Should().Be(2);

        (await svc.AssignSessionDayAsync(T1, false, 5, null)).Should().BeTrue();
        (await svc.GetSessionPlanDaysAsync([5])).Should().BeEmpty();
    }
    [Fact]
    public async Task Rotate_Assigns_Days_In_Order_And_Keeps_Other_Plan_Choices()
    {
        var (f, _) = TestDb.CreateFresh();
        await SeedAsync(f);
        var svc = Make(f);
        var dto = NewPlan();
        dto.Days.Add(new PlanDayEditDto { Label = "Dzień B", Exercises = [new PlanExerciseEditDto { ExerciseId = 3, Sets = 3, Reps = "5" }] });
        var planId = await svc.SavePlanAsync(T1, dto);
        var other = NewPlan(); other.Name = "Inny";
        var otherId = await svc.SavePlanAsync(T1, other);
        int a, b, otherDay;
        await using (var db = f.CreateDbContext())
        {
            var days = db.PlanDays.Where(d => d.PlanId == planId).OrderBy(d => d.Order).Select(d => d.Id).ToList();
            a = days[0]; b = days[1];
            otherDay = db.PlanDays.Single(d => d.PlanId == otherId).Id;
            for (var i = 0; i < 5; i++)
                db.Sessions.Add(new Session { Id = 10 + i, ClientId = 1, SessionTypeId = 1, TrainerUserId = T1, StartTime = new DateTime(2026, 1, 5 + i, 10, 0, 0), PlanDayId = i == 3 ? otherDay : null });
            db.Sessions.Add(new Session { Id = 20, ClientId = 1, SessionTypeId = 1, TrainerUserId = T1, StartTime = new DateTime(2026, 1, 7, 12, 0, 0), Status = SessionStatus.Cancelled });
            await db.SaveChangesAsync();
        }

        var n = await svc.RotateUpcomingAsync(T1, false, 11, b);

        n.Should().Be(3);
        var map = await svc.GetSessionPlanDaysAsync([10, 11, 12, 13, 14, 20]);
        map.ContainsKey(10).Should().BeFalse();       // wcześniejsza wizyta bez zmian
        map[11].PlanDayId.Should().Be(b);
        map[12].PlanDayId.Should().Be(a);
        map[13].PlanDayId.Should().Be(otherDay);      // świadomy wybór z innego planu zostaje
        map[14].PlanDayId.Should().Be(b);
        map.ContainsKey(20).Should().BeFalse();       // odwołana pominięta
        (await svc.RotateUpcomingAsync(T2, false, 11, a)).Should().Be(0);
    }
}
