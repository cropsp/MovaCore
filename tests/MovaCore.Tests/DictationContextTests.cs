using MovaCore.Models;
using MovaCore.Services;
using Xunit;

namespace MovaCore.Tests
{
    public class DictationContextTests
    {
        private readonly FakeDictationTarget _target = new();

        [Fact]
        public void RememberedText_IsTheTextBeforeTheCaret()
        {
            using var context = new DictationContext(_target);

            context.Remember("Я думаю, що");

            Assert.Equal("Я думаю, що", context.TextBefore());
            Assert.True(_target.Watching);
        }

        [Fact]
        public void NothingRemembered_IsUnknown()
        {
            using var context = new DictationContext(_target);

            Assert.Null(context.TextBefore());
            Assert.False(_target.Watching);
        }

        // A key press or a click may have moved the caret
        [Fact]
        public void Interruption_ForgetsTheText()
        {
            using var context = new DictationContext(_target);
            context.Remember("Я думаю, що");

            _target.Interrupt();

            Assert.Null(context.TextBefore());
            Assert.False(_target.Watching);
        }

        [Fact]
        public void AnotherWindowOrControl_ForgetsTheText()
        {
            using var context = new DictationContext(_target);
            context.Remember("Я думаю, що");

            _target.Focus = new DictationFocus(1, 3);

            Assert.Null(context.TextBefore());
            _target.Focus = new DictationFocus(1, 2);
            Assert.Null(context.TextBefore()); // forgotten for good
        }

        [Fact]
        public void UnknownFocus_RemembersNothing()
        {
            using var context = new DictationContext(_target);
            _target.Focus = null;

            context.Remember("Я думаю, що");
            _target.Focus = new DictationFocus(1, 2);

            Assert.Null(context.TextBefore());
        }

        [Fact]
        public async Task OldText_IsForgotten()
        {
            using var context = new DictationContext(_target) { MaxAge = TimeSpan.FromMilliseconds(50) };
            context.Remember("Я думаю, що");

            await Task.Delay(100);

            Assert.Null(context.TextBefore());
        }

        [Fact]
        public void OnlyTheEndOfALongTextIsKept()
        {
            using var context = new DictationContext(_target);

            context.Remember(new string('а', 200) + " кінець");

            Assert.EndsWith(" кінець", context.TextBefore());
            Assert.True(context.TextBefore()!.Length <= 64);
        }
    }

    public class FakeDictationTarget : IDictationTarget
    {
        public DictationFocus? Focus { get; set; } = new DictationFocus(1, 2);
        public bool Watching { get; private set; }

        public event EventHandler? Interrupted;

        public DictationFocus? GetFocus() => Focus;

        public void WatchClicks(bool watch) => Watching = watch;

        public void Interrupt() => Interrupted?.Invoke(this, EventArgs.Empty);
    }
}
