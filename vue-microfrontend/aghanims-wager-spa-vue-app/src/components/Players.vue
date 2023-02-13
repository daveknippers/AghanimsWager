<template>
  <div class="players">
    <b-container>
      <b-row class="mt-4">
        <b-col cols="4">
          <b-card title="Mister Moneybags"
            img-src="https://carboncostume.com/wordpress/wp-content/uploads/2013/10/monopoly-650x350.jpg"
            img-alt="Image" img-top tag="article" class="mb-2">
            <b-card-text>
              <h2>{{ topSaltUser }}</h2><br />
              Most salt, who did you bribe?
            </b-card-text>
          </b-card>
        </b-col>
        <b-col cols="4">
          <b-card title="Grim Reaper's best friend"
            img-src="https://i.kym-cdn.com/entries/icons/original/000/011/121/SKULL_TRUMPET_0-1_screenshot.png"
            img-alt="Image" img-top tag="article" class="mb-2">
            <b-card-text>
              <h2>{{ topDeathsUser }}</h2><br />
              Sometimes the death timer cooldown is the only thing keeping that number from being higher.
            </b-card-text>
          </b-card>
        </b-col>
        <b-col cols="4">
          <b-card title="Biggest fuckin' nerd"
            img-src="https://media.istockphoto.com/photos/human-palm-touching-lawn-grass-low-angle-view-picture-id1349781282?k=20&m=1349781282&s=612x612&w=0&h=B7Uo9H1LAiG5_70747QgDDHculRCqPuZTQIC52gHJTA="
            img-alt="Image" img-top tag="article" class="mb-2">
            <b-card-text>
              <h2>{{ mostPlayedUser }}</h2><br />
              Stop playing so much dota go touch grass
            </b-card-text>
          </b-card>
        </b-col>
        <b-col cols="6">
          <b-card class="mt-4">
            <h4>Leaderboard</h4>
            <b-table striped hover :items="balancesLookup" :fields="leaderboardFields"></b-table>
          </b-card>
        </b-col>
        <b-col cols="6">
          <b-card class="mt-4">
            <h4>Feederboard</h4>
            <b-table striped hover :items="deathsLookup" :fields="feederboardFields"></b-table>
          </b-card>
        </b-col>
      </b-row>
    </b-container>
  </div>
</template>

<script>
export default {
  name: 'PlayersPage',
  data() {
    return {
      maxValue: '',
      mostPlayedUser: '',
      balances: [],
      feederboard: [],
      discordIds: [],
      feederboardFields: [
      {
          key: 'discordName',
          label: 'Discord Name',
          sortable: false
        },
        {
          key: 'deaths',
          label: 'Deaths',
          sortable: false
        },
        {
          key: 'rank',
          label: 'Rank',
          sortable: false
        }
      ],
      leaderboardFields: [
        {
          key: 'discordName',
          label: 'Discord Name',
          sortable: false
        },
        {
          key: 'tokens',
          label: 'Salt',
          sortable: false
        }
      ]
    }
  },
  mounted() {
    this.getDiscordIds();
    this.getBalanceLedger();
    this.getFeederboard();
    this.getMostPlayedUser();
  },
  computed: {
    balancesLookup() {
      return this.balances.map(balance => {
        let matchingObject = this.discordIds.find(x => x.discordId === balance.discordId);
        if (matchingObject) {
          balance.discordName = matchingObject.discordName;
        }
        return balance;
      });
    },
    deathsLookup() {
      return this.feederboard;
    },
    topSaltUser() {
      let reduced = {};
      if (this.balancesLookup.length > 0) {
        reduced = this.balancesLookup.reduce(function (a, b) {
          return (a.tokens > b.tokens) ? a : b;
        });
      }
      return reduced.discordName;
    },
    topDeathsUser() {
      let reduced = {};
      if (this.deathsLookup.length > 0) {
        reduced = this.deathsLookup.reduce(function (a, b) {
          return (a.deaths > b.deaths) ? a : b;
        });
      }
      return reduced.discordName;
    }
  },
  methods: {
    getBalanceLedger() {
      fetch('/api/BalanceLedger')
        .then(function (response) {
          if (response.status != 200) {
            throw response.status;
          } else {
            return response.json();
          }
        }.bind(this))
        .then(function (data) {
          // remove the invalid discord IDs and order by tokens desc
          this.balances = data.sort((a, b) => b.tokens > a.tokens).filter(item => item.discordId > 0);
        }.bind(this))
        .catch(error => {
          console.error(error);
        });
    },
    getFeederboard() {
      fetch('/api/DiscordCommands/Feederboard')
        .then(function (response) {
          if (response.status != 200) {
            throw response.status;
          } else {
            return response.json();
          }
        }.bind(this))
        .then(function (data) {
          // remove the invalid discord IDs and order by deaths desc
          this.feederboard = data.sort((a, b) => b.deaths > a.deaths).filter(item => item.deaths > 0);
        }.bind(this))
        .catch(error => {
          console.error(error);
        });
    },
    getMostPlayedUser() {
      fetch('/api/DiscordCommands/MostGamesPlayed')
        .then(function (response) {
          if (response.status != 200) {
            throw response.status;
          } else {
            return response.text();
          }
        }.bind(this))
        .then(function (data) {
          this.mostPlayedUser = data;
        }.bind(this))
        .catch(error => {
          console.error(error);
        });
    },
    getDiscordIds() {
      fetch('/api/DiscordId')
        .then(function (response) {
          if (response.status != 200) {
            throw response.status;
          } else {
            return response.json();
          }
        }.bind(this))
        .then(function (data) {
          // remove the invalid discord IDs and order by tokens desc
          this.discordIds = data.sort((a, b) => a.discordId > b.discordId);
        }.bind(this))
        .catch(error => {
          console.error(error);
        });
    }
  }
}
</script>

<!-- Add "scoped" attribute to limit CSS to this component only -->
<style scoped>
h3 {
  margin: 40px 0 0;
}

ul {
  list-style-type: none;
  padding: 0;
}

li {
  display: inline-block;
  margin: 0 10px;
}

a {
  color: #42b983;
}
</style>
