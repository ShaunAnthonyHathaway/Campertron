// Campertron's web UI: the results page's automatic progress (decision D108; business-rules-settings §34).
//
// While a search of this browser session runs, the page asks the web UI for the run's state, one request at a time, and
// shows it in place: the status, the stage, the counts and the elapsed time, the bar and each chosen file's state. When the
// run has ended, it takes the page's results part from the page itself (one GET of /results) and puts it in place of the
// old one, then stops. It only looks: every request is a GET, which never starts, repeats or cancels a search, and nothing
// is sent while a form is sent.
//
// What it never does: run code or markup from an answer (it sets text, attributes of the bar and, once, the results part
// the web UI rendered), move the focus or the scroll position while the run goes on, keep a clock of its own (the elapsed
// time is the web UI's), or show an older answer over a newer one: an answer counts only for the run the page shows (its
// key), never after the page stopped, and only when it is newer (a later state, or the same state with a higher number).
// Its words are the page's (the attributes of #live); the time of the last update is the browser's own.
//
// No library, nothing from another site, no inline code: the web UI serves this file itself, for a signed-in session only,
// under its Content-Security-Policy (script-src 'self', connect-src 'self').
(function () {
  'use strict';

  var Rank = { running: 1, canceling: 2, ended: 3 };
  var LongestText = 8192;
  var MostRows = 4096;
  // a sentinel for an answer that sends the browser to the sign-in (the session ended)
  var SignedOut = {};
  // the one observer of the page, or null
  var Current = null;

  function number(Text, Least, Most, Otherwise) {
    var Value = Number(Text);
    return isFinite(Value) && Value >= Least && Value <= Most ? Math.floor(Value) : Otherwise;
  }

  function isText(Value) {
    return typeof Value === 'string' && Value.length <= LongestText;
  }

  function isCount(Value) {
    return typeof Value === 'number' && isFinite(Value) && Value >= 0;
  }

  function setText(Element, Text) {
    if (Element && Element.textContent !== Text) {
      Element.textContent = Text;
    }
  }

  // an answer of the web UI, checked whole before any part of it is shown
  function valid(Answer) {
    if (!Answer || typeof Answer !== 'object' || Answer.format !== 1 || !isCount(Answer.seq)) {
      return false;
    }
    if (Answer.state === 'replaced') {
      return true;
    }
    if (!isText(Answer.run)) {
      return false;
    }
    if (Answer.state === 'ended') {
      return true;
    }
    if (Answer.state !== 'running' && Answer.state !== 'canceling') {
      return false;
    }
    if (!isText(Answer.status) || !(Answer.details === undefined || isText(Answer.details))) {
      return false;
    }
    var Bar = Answer.bar;
    if (!Bar || typeof Bar !== 'object' || typeof Bar.indeterminate !== 'boolean' || !isCount(Bar.value) || !isCount(Bar.max) || Bar.max <= 0) {
      return false;
    }
    if (!Array.isArray(Answer.rows) || Answer.rows.length > MostRows) {
      return false;
    }
    for (var i = 0; i < Answer.rows.length; i++) {
      var Row = Answer.rows[i];
      if (!Row || typeof Row !== 'object' || !isCount(Row.row) || Math.floor(Row.row) !== Row.row || !isText(Row.state)) {
        return false;
      }
    }
    return true;
  }

  function Observer(Live) {
    this.Live = Live;
    this.Run = Live.getAttribute('data-run');
    this.Rank = Rank[Live.getAttribute('data-state')] || Rank.running;
    this.Seq = number(Live.getAttribute('data-seq'), 0, Number.MAX_SAFE_INTEGER, 0);
    this.Interval = number(Live.getAttribute('data-interval'), 50, 60000, 2000);
    this.Timeout = number(Live.getAttribute('data-timeout'), 200, 120000, 10000);
    this.Words = {
      updating: Live.getAttribute('data-updating') || '',
      resumed: Live.getAttribute('data-resumed') || '',
      failing: Live.getAttribute('data-failing') || '',
      endedFailing: Live.getAttribute('data-ended-failing') || '',
      signedOut: Live.getAttribute('data-signed-out') || '',
      outOfDate: Live.getAttribute('data-out-of-date') || ''
    };
    this.Failures = 0;
    this.Failing = false;
    this.Stopped = false;
    this.Timer = null;
    // the request in flight (its AbortController), or null: never two at a time
    this.Request = null;
    // the page itself was the last update: the web UI sent it just now
    this.LastUpdate = new Date().toLocaleTimeString();
    this.show('hint', this.Words.updating);
  }

  // the line under the bar: its style (a message's style says its severity in words too) and its text with the time
  Observer.prototype.show = function (Style, Text) {
    if (this.Live.className !== Style) {
      this.Live.className = Style;
    }
    setText(this.Live, Text.split('{time}').join(this.LastUpdate));
  };

  // what is announced: only a change the user needs to know of (the updates fail, come again or stop, the run ended);
  // never an ordinary update
  function announce(Text) {
    var Region = document.getElementById('results-announce');
    if (Region && Text) {
      setText(Region, Text);
    }
  }

  Observer.prototype.schedule = function (Delay) {
    var Self = this;
    if (this.Stopped || this.Timer !== null) {
      return;
    }
    this.Timer = window.setTimeout(function () {
      Self.Timer = null;
      if (Self.Rank === Rank.ended) {
        Self.finish();
      } else {
        Self.poll();
      }
    }, Delay);
  };

  // one GET, at most Timeout long; Then gets the response, or null when it failed
  Observer.prototype.ask = function (Address, Accept, Read, Then) {
    var Self = this;
    if (this.Stopped || this.Request) {
      return;
    }
    var Control = new AbortController();
    this.Request = Control;
    var Limit = window.setTimeout(function () { Control.abort(); }, this.Timeout);
    function done(Value) {
      window.clearTimeout(Limit);
      //an answer to a request this page no longer waits for (stopped, or the results part replaced) is dropped
      if (Self.Request !== Control || Self.Stopped) {
        return;
      }
      Self.Request = null;
      Then(Value);
    }
    window.fetch(Address, {
      method: 'GET',
      credentials: 'same-origin',
      cache: 'no-store',
      redirect: 'manual',
      headers: { 'Accept': Accept },
      signal: Control.signal
    }).then(function (Response) {
      if (Response.type === 'opaqueredirect' || Response.status === 401 || Response.status === 403) {
        return SignedOut;
      }
      var Type = (Response.headers.get('Content-Type') || '').toLowerCase();
      if (!Response.ok || Type.indexOf(Accept) !== 0) {
        throw new Error('unusable answer');
      }
      return Read(Response);
    }).then(done, function () { done(null); });
  };

  // the run's state: applied only when it is the page's run's, and newer than what the page shows
  Observer.prototype.poll = function () {
    var Self = this;
    this.ask('/results?handler=progress&run=' + encodeURIComponent(this.Run), 'application/json',
      function (Response) { return Response.json(); },
      function (Answer) {
        if (Answer === SignedOut) {
          Self.stop(Self.Words.signedOut);
        } else if (!valid(Answer)) {
          Self.failed();
        } else if (Answer.state === 'replaced' || Answer.run !== Self.Run) {
          Self.stop(Self.Words.outOfDate);
        } else {
          var Next = Rank[Answer.state];
          if (Next < Self.Rank || (Next === Self.Rank && Answer.seq <= Self.Seq)) {
            //older than, or the same as, what the page shows: nothing of it is shown, but the web UI answers
            Self.healthy();
            Self.schedule(Self.Interval);
            return;
          }
          if (Next !== Rank.ended && !Self.apply(Answer)) {
            Self.failed();
            return;
          }
          Self.Rank = Next;
          Self.Seq = Answer.seq;
          Self.healthy();
          if (Next === Rank.ended) {
            Self.finish();
          } else {
            Self.schedule(Self.Interval);
          }
        }
      });
  };

  // the run's state in place: text and the bar's attributes only; no focus, no scroll, no control touched
  Observer.prototype.apply = function (Answer) {
    var Status = document.getElementById('status');
    var Bar = document.getElementById('progress');
    if (!Status || !Bar) {
      return false;
    }
    setText(Status, Answer.status);
    if (typeof Answer.details === 'string') {
      setText(document.getElementById('progress-details'), Answer.details);
    }
    if (Answer.bar.indeterminate) {
      Bar.removeAttribute('value');
      Bar.removeAttribute('max');
    } else {
      Bar.setAttribute('max', String(Answer.bar.max));
      Bar.setAttribute('value', String(Math.min(Answer.bar.value, Answer.bar.max)));
    }
    for (var i = 0; i < Answer.rows.length; i++) {
      setText(document.getElementById('file-' + Answer.rows[i].row + '-state'), Answer.rows[i].state);
    }
    return true;
  };

  Observer.prototype.healthy = function () {
    var Was = this.Failing;
    this.Failures = 0;
    this.Failing = false;
    this.LastUpdate = new Date().toLocaleTimeString();
    this.show('hint', this.Words.updating);
    if (Was) {
      announce(this.Words.resumed);
    }
  };

  // no answer, or one the page can't use: said at once, never as the search's failure; tried again later and later
  Observer.prototype.failed = function () {
    var Was = this.Failing;
    this.Failures++;
    this.Failing = true;
    var Text = this.Rank === Rank.ended ? this.Words.endedFailing : this.Words.failing;
    this.show('message warning', Text);
    if (!Was) {
      announce(this.Live.textContent);
    }
    this.schedule(Math.min(this.Interval * Math.pow(2, this.Failures), this.Interval * 15));
  };

  // the run has ended: the page's results part, as the web UI renders it now, in place of the old one
  Observer.prototype.finish = function () {
    var Self = this;
    this.ask('/results', 'text/html',
      function (Response) { return Response.text(); },
      function (Text) {
        if (Text === SignedOut) {
          Self.stop(Self.Words.signedOut);
        } else if (typeof Text !== 'string' || !Self.swap(Text)) {
          Self.failed();
        }
      });
  };

  Observer.prototype.swap = function (Text) {
    var Next = new DOMParser().parseFromString(Text, 'text/html').getElementById('results-view');
    var Here = document.getElementById('results-view');
    if (!Next || !Here || !Here.parentNode) {
      return false;
    }
    var Focused = document.activeElement;
    var Inside = !!Focused && Focused !== document.body && Here.contains(Focused);
    var FocusId = Inside ? Focused.id : '';
    var X = window.scrollX;
    var Y = window.scrollY;
    this.halt();
    Here.parentNode.replaceChild(document.importNode(Next, true), Here);
    window.scrollTo(X, Y);
    //the focus was on a control of the old part (Cancel, the link): it goes to the same control, else to the status
    if (Inside) {
      var Target = (FocusId && document.getElementById(FocusId)) || document.getElementById('status');
      if (Target) {
        Target.focus({ preventScroll: true });
      }
    }
    var Status = document.getElementById('status');
    if (Status) {
      announce(Status.textContent);
    }
    //the page may show a run again (another tab of this session started one): its own observer
    begin();
    return true;
  };

  // no more timers or requests of this observer, and no answer applied after this
  Observer.prototype.halt = function () {
    this.Stopped = true;
    if (this.Timer !== null) {
      window.clearTimeout(this.Timer);
      this.Timer = null;
    }
    if (this.Request) {
      var Request = this.Request;
      this.Request = null;
      Request.abort();
    }
    if (Current === this) {
      Current = null;
    }
  };

  // the updates stop for good: Text says why (null: the page is being left, nothing to say)
  Observer.prototype.stop = function (Text) {
    this.halt();
    if (Text) {
      this.show('message warning', Text);
      announce(this.Live.textContent);
    }
  };

  function begin() {
    if (Current) {
      return;
    }
    var Live = document.getElementById('live');
    if (!Live || !Live.getAttribute('data-run') || !window.fetch || !window.AbortController || !window.DOMParser) {
      return;
    }
    Current = new Observer(Live);
    Current.schedule(Current.Interval);
  }

  //a form that is sent leaves the page (Cancel, Start, the choice, Sign out): nothing more is asked meanwhile
  document.addEventListener('submit', function () {
    if (Current) {
      Current.stop(null);
    }
  }, true);
  window.addEventListener('pagehide', function () {
    if (Current) {
      Current.stop(null);
    }
  });
  //a page the browser shows again from its history goes on where it is
  window.addEventListener('pageshow', function (Event) {
    if (Event.persisted) {
      begin();
    }
  });

  begin();
}());
