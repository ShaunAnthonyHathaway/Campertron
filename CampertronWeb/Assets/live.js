// Campertron's web UI: the automatic progress of a page whose operation goes on (decisions D108 and D109;
// business-rules-settings §34, §35, §36 and §39). The web UI's one script: the results page's run, the system page's
// refresh of the campground data and the monitoring page's monitoring use it alike.
//
// While the page shows an operation that goes on (#live names it and holds the page's words), the page asks the web UI
// for its state, one request at a time ("?handler=progress" of the page itself), and shows it in place: the text of the
// page's own elements the answer names and the bar's values. When the answer's view is not the page's (a control, a
// section or the operation's end changed), it takes the page itself once more ("?handler=view" of the page's own address)
// and puts the parts that changed in place of the old ones; when the operation has ended, that is its last request. It
// only looks: every request is a GET, which never starts, repeats, resumes or cancels anything and never renews the
// browser session, and nothing is asked while a form is sent.
//
// What it never does: run code or markup from an answer (it sets text, attributes of the bar and the parts the web UI
// rendered for the page), move the focus or the scroll position while the operation goes on, keep a clock of its own (an
// elapsed time is the web UI's), or show an older answer over a newer one: an answer counts only for the operation the
// page shows (its key), never after the page stopped, only when its number is higher, and never with an earlier state
// (a lower rank). It announces only what changes the user's view: the updates fail, come back or stop, the state changes
// (Cancel, a start, a stop, the end). Its words are the page's (the attributes of #live); the time of the last update is
// the browser's own.
//
// No library, nothing from another site, no inline code: the web UI serves this file itself, for a signed-in session only,
// under its Content-Security-Policy (script-src 'self', connect-src 'self').
(function () {
  'use strict';

  var LongestText = 8192;
  var MostFields = 4096;
  var FieldName = /^[a-z][a-z0-9-]{0,63}$/;
  var StateName = /^[a-z]{1,32}$/;
  // a sentinel for an answer that sends the browser to the sign-in (the session ended)
  var SignedOut = {};
  // the one observer of the page, or null
  var Current = null;

  function number(Text, Least, Most, Otherwise) {
    var Value = Number(Text);
    return Text !== null && Text !== '' && isFinite(Value) && Value >= Least && Value <= Most ? Math.floor(Value) : Otherwise;
  }

  function isText(Value) {
    return typeof Value === 'string' && Value.length <= LongestText;
  }

  function isCount(Value) {
    return typeof Value === 'number' && isFinite(Value) && Value >= 0;
  }

  function isWhole(Value) {
    return isCount(Value) && Math.floor(Value) === Value;
  }

  function setText(Element, Text) {
    if (Element && Element.textContent !== Text) {
      Element.textContent = Text;
    }
  }

  // an answer of the web UI, checked whole before any part of it is shown
  function valid(Answer) {
    if (!Answer || typeof Answer !== 'object' || Answer.format !== 2 || !isWhole(Answer.seq)) {
      return false;
    }
    if (Answer.state === 'replaced') {
      return true;
    }
    if (!isText(Answer.key) || typeof Answer.state !== 'string' || !StateName.test(Answer.state) || !isWhole(Answer.rank)) {
      return false;
    }
    if (Answer.state === 'ended') {
      return true;
    }
    if (!isText(Answer.view)) {
      return false;
    }
    var Fields = Answer.fields;
    if (!Fields || typeof Fields !== 'object' || Array.isArray(Fields)) {
      return false;
    }
    var Names = Object.keys(Fields);
    if (Names.length > MostFields) {
      return false;
    }
    for (var i = 0; i < Names.length; i++) {
      if (!FieldName.test(Names[i]) || !isText(Fields[Names[i]])) {
        return false;
      }
    }
    var Bar = Answer.bar;
    if (Bar !== undefined && (!Bar || typeof Bar !== 'object' || typeof Bar.indeterminate !== 'boolean' || !isCount(Bar.value) || !isCount(Bar.max) || Bar.max <= 0)) {
      return false;
    }
    return true;
  }

  // the page's own address with one handler of its own: its answer, or the page as a GET shows it
  function address(Handler, Key) {
    var Query = new URLSearchParams(Handler === 'view' ? window.location.search : '');
    Query.set('handler', Handler);
    if (Key !== null) {
      Query.set('key', Key);
    }
    return window.location.pathname + '?' + Query.toString();
  }

  function Observer(Live) {
    this.Live = Live;
    this.Key = Live.getAttribute('data-key');
    this.State = Live.getAttribute('data-state') || '';
    this.Rank = number(Live.getAttribute('data-rank'), 0, 1000, 0);
    this.Seq = number(Live.getAttribute('data-seq'), 0, Number.MAX_SAFE_INTEGER, 0);
    this.View = Live.getAttribute('data-view') || '';
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
    // the page itself is due (null, 'changed' or 'ended'), and the state before the answer that said so
    this.Pending = null;
    this.Before = this.State;
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

  // what is announced: only a change the user needs to know of; never an ordinary update
  function announce(Text) {
    var Region = document.getElementById('live-announce');
    if (Region && Text) {
      setText(Region, Text);
    }
  }

  // the status as a reader hears it: its words, without the markup's line breaks
  function statusText() {
    var Status = document.getElementById('status');
    return Status ? Status.textContent.replace(/\s+/g, ' ').trim() : '';
  }

  Observer.prototype.schedule = function (Delay) {
    var Self = this;
    if (this.Stopped || this.Timer !== null) {
      return;
    }
    this.Timer = window.setTimeout(function () {
      Self.Timer = null;
      if (Self.Pending) {
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
      //an answer to a request this page no longer waits for (stopped, or its parts replaced) is dropped
      if (Self.Request !== Control || Self.Stopped) {
        return;
      }
      Self.Request = null;
      try {
        Then(Value);
      } catch (Failure) {
        //an answer the page could not apply is said as a failed update, never left as a page that looks current
        if (!Self.Stopped && Self.Request === null && Self.Timer === null) {
          Self.failed();
        }
      }
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

  // the operation's state: applied only when it is the page's operation's, newer than what the page shows and not an
  // earlier state; a new view or the end takes the page itself
  Observer.prototype.poll = function () {
    var Self = this;
    this.ask(address('progress', this.Key), 'application/json',
      function (Response) { return Response.json(); },
      function (Answer) {
        if (Answer === SignedOut) {
          Self.stop(Self.Words.signedOut);
        } else if (!valid(Answer)) {
          Self.failed();
        } else if (Answer.state === 'replaced' || Answer.key !== Self.Key) {
          Self.stop(Self.Words.outOfDate);
        } else if (Answer.seq <= Self.Seq || Answer.rank < Self.Rank) {
          //older than, or the same as, what the page shows, or an earlier state: nothing of it is shown, but the web UI
          //answers
          Self.healthy();
          Self.schedule(Self.Interval);
        } else if (Answer.state === 'ended' || Answer.view !== Self.View) {
          Self.Before = Self.State;
          Self.Pending = Answer.state === 'ended' ? 'ended' : 'changed';
          Self.State = Answer.state;
          Self.Rank = Answer.rank;
          Self.Seq = Answer.seq;
          Self.healthy();
          Self.finish();
        } else {
          Self.apply(Answer);
          var Changed = Answer.state !== Self.State;
          Self.State = Answer.state;
          Self.Rank = Answer.rank;
          Self.Seq = Answer.seq;
          Self.healthy();
          if (Changed) {
            announce(statusText());
          }
          Self.schedule(Self.Interval);
        }
      });
  };

  // the operation's state in place: the text of the page's elements the answer names and the bar's attributes only; no
  // focus, no scroll, no control touched. An element the page has not is left out
  Observer.prototype.apply = function (Answer) {
    var View = document.getElementById('live-view');
    if (!View) {
      throw new Error('no view');
    }
    var Names = Object.keys(Answer.fields);
    for (var i = 0; i < Names.length; i++) {
      var Element = document.getElementById(Names[i]);
      if (Element && Element !== this.Live && View.contains(Element)) {
        setText(Element, Answer.fields[Names[i]]);
      }
    }
    var Bar = document.getElementById('progress');
    if (Answer.bar && Bar && View.contains(Bar)) {
      if (Answer.bar.indeterminate) {
        Bar.removeAttribute('value');
        Bar.removeAttribute('max');
      } else {
        Bar.setAttribute('max', String(Answer.bar.max));
        Bar.setAttribute('value', String(Math.min(Answer.bar.value, Answer.bar.max)));
      }
    }
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

  // no answer, or one the page can't use: said at once, never as the operation's failure; tried again later and later
  Observer.prototype.failed = function () {
    var Was = this.Failing;
    this.Failures++;
    this.Failing = true;
    this.show('message warning', this.Pending === 'ended' ? this.Words.endedFailing : this.Words.failing);
    if (!Was) {
      announce(this.Live.textContent);
    }
    this.schedule(Math.min(this.Interval * Math.pow(2, this.Failures), this.Interval * 15));
  };

  // the page itself, as the web UI renders it now: its changed parts in place of the old ones
  Observer.prototype.finish = function () {
    var Self = this;
    this.ask(address('view', null), 'text/html',
      function (Response) { return Response.text(); },
      function (Text) {
        if (Text === SignedOut) {
          Self.stop(Self.Words.signedOut);
        } else if (typeof Text !== 'string' || !Self.swap(Text)) {
          Self.failed();
        }
      });
  };

  // the parts of a page (the direct children of #live-view), their ids and their names (data-part)
  function sameParts(Old, New) {
    if (Old.length !== New.length) {
      return false;
    }
    for (var i = 0; i < Old.length; i++) {
      if (Old[i].tagName !== New[i].tagName || Old[i].id !== New[i].id || Old[i].hasAttribute('data-part') !== New[i].hasAttribute('data-part')) {
        return false;
      }
    }
    return true;
  }

  Observer.prototype.swap = function (Text) {
    var Next = new DOMParser().parseFromString(Text, 'text/html').getElementById('live-view');
    var Here = document.getElementById('live-view');
    if (!Next || !Here || !Here.parentNode) {
      return false;
    }
    var Focused = document.activeElement;
    var FocusId = Focused && Focused !== document.body ? Focused.id : '';
    var X = window.scrollX;
    var Y = window.scrollY;
    var Was = this.Before;
    var Recovered = this.Failing;
    var Live = this.Live;
    this.halt();
    var Old = Array.prototype.slice.call(Here.children);
    var New = Array.prototype.slice.call(Next.children);
    var Removed = [];
    if (sameParts(Old, New)) {
      //a part whose name is the same shows the same: it stays, and so does the focus in it; the part with #live always
      //changes (its state, view and number are the new page's)
      for (var i = 0; i < Old.length; i++) {
        var Part = New[i].getAttribute('data-part');
        if (Part === null || Part !== Old[i].getAttribute('data-part') || Old[i].contains(Live)) {
          Here.replaceChild(document.importNode(New[i], true), Old[i]);
          Removed.push(Old[i]);
        }
      }
    } else {
      Here.parentNode.replaceChild(document.importNode(Next, true), Here);
      Removed.push(Here);
    }
    window.scrollTo(X, Y);
    //the focus was on a control of a replaced part (Cancel, Stop, a link): it goes to the same control, else to the
    //status; never anywhere when it was not in a replaced part
    var Inside = !!Focused && Focused !== document.body && Removed.some(function (Gone) { return Gone.contains(Focused); });
    if (Inside) {
      var Same = FocusId ? document.getElementById(FocusId) : null;
      if (Same) {
        Same.focus({ preventScroll: true });
      }
      if (!Same || document.activeElement !== Same) {
        var Status = document.getElementById('status');
        if (Status) {
          Status.focus({ preventScroll: true });
        }
      }
    }
    var NewLive = document.getElementById('live');
    var Now = NewLive ? NewLive.getAttribute('data-state') : 'ended';
    if (Now !== Was) {
      announce(statusText());
    } else if (Recovered) {
      announce(this.Words.resumed);
    }
    //the page may show an operation that goes on (the same, or one another tab started): its own observer
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
    if (!Live || !Live.getAttribute('data-key') || !window.fetch || !window.AbortController || !window.DOMParser || !window.URLSearchParams) {
      return;
    }
    Current = new Observer(Live);
    Current.schedule(Current.Interval);
  }

  //a form that is sent leaves the page (Cancel, Start, Stop, the choice, Sign out): nothing more is asked meanwhile
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
