<template>
  <div class="matches">
    <b-container>
      <b-row class="mt-4">
        <b-col style="display: flex; flex-wrap: wrap;">
          <b-card title="Best Bromance" img-top img-src="/assets/bestBromance.jpg" img-height="300px" tag="article"
            class="mb-2" style="flex: 1 0 200px;">
            <b-card-text>
              <h2>{{ bestBromance.bro1Name }}, {{ bestBromance.bro2Name }}</h2>
              <h2>Win Rate: {{ (bestBromance.winRate * 100).toFixed(2) }}%</h2><br />
              We finish each other's sandwiches
            </b-card-text>
          </b-card>
          <b-card title="Worst Bromance" img-top img-src="/assets/worstBromance.png" img-height="300px" tag="article"
            class="mb-2" style="flex: 1 0 200px;">
            <b-card-text>
              <h2>{{ worstBromance.bro1Name }}, {{ worstBromance.bro2Name }}</h2>
              <h2>Win Rate: {{ (worstBromance.winRate * 100).toFixed(2) }}%</h2><br />
              It may be time for some trust falls
            </b-card-text>
          </b-card>
        </b-col>
      </b-row>
      <b-row>
        <b-col style="display: flex; flex-wrap: wrap;">
          <div style="flex: 1 0 300px;">
            <h4>Bromances</h4>
            <ol>
              <li class="flex-row" style="background-color: gray;">
                <span class="flex-child matches-header">Bros Discord Names</span>
                <span class="flex-child matches-header" style="text-align: right;">Won Matches</span>
                <span class="flex-child matches-header" style="text-align: right;">Total Matches</span>
                <span class="flex-child matches-header" style="text-align: right;">Win Rate</span>
              </li>
              <li class="flex-row" v-for="bromance, index in bromances" :key="index">
                <span class="flex-child matches-descriptors">{{ bromance.bro1Name }}, {{ bromance.bro2Name }}</span>
                <span class="flex-child matches-data">{{ bromance.totalWins }}</span>
                <span class="flex-child matches-data">{{ bromance.totalMatches }}</span>
                <span class="flex-child matches-data">{{ (bromance.winRate * 100).toFixed(2) }}%</span>
              </li>
            </ol>
          </div>
        </b-col>
      </b-row>
    </b-container>
  </div>
</template>

<script>
export default {
  name: 'MatchesPage',
  data() {
    return {
      bromances: []
    }
  },
  mounted() {
    this.getBromances();
  },
  computed: {
    bestBromance() {
      let reduced = {};
      if (this.bromances.length > 0) {
        reduced = this.bromances.reduce(function (a, b) {
          return (a.winRate > b.winRate || (a.winRate == b.winRate && a.totalWins > b.totalWins)) ? a : b;
        });
      }
      return reduced;
    },
    worstBromance() {
      let reduced = {};
      if (this.bromances.length > 0) {
        reduced = this.bromances.reduce(function (a, b) {
          return (a.winRate < b.winRate || (a.winRate == b.winRate && a.totalMatches > b.totalMatches)) ? a : b;
        });
      }
      return reduced;
    }
  },
  methods: {
    getBromances() {
      fetch('/api/DiscordCommands/Bromance')
        .then(function (response) {
          if (response.status != 200) {
            throw response.status;
          } else {
            return response.json();
          }
        }.bind(this))
        .then(function (data) {
          this.bromances = data.sort((a, b) => b.winRate - a.winRate);
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
.flex-row {
  border: 1px solid grey;
  border-radius: 25px;
  display: flex;
  justify-content: space-evenly;
  padding-left: 25px;
  padding-right: 25px;
}

div ::v-deep(.matches-header) {
  font-family: Arial, Helvetica, sans-serif;
  font-style: normal;
  font-weight: 500;
  font-size: 24px;
  text-align: left;
  vertical-align: middle;
  margin-top: 4px;
  flex-grow: 4;
  color: white;
}

div ::v-deep(.matches-descriptors) {
  font-family: Arial, Helvetica, sans-serif;
  font-style: normal;
  font-weight: 400;
  font-size: 18px;
  text-align: left;
  vertical-align: middle;
  margin-top: 4px;
  flex: 1;
}

div ::v-deep(.matches-data) {
  font-family: monospace;
  font-style: normal;
  font-weight: 500;
  font-size: 24px;
  text-align: right;
  vertical-align: middle;
  flex: 1;
}

h3 {
  margin: 40px 0 0;
}

ul {
  list-style-type: none;
  padding: 0;
}

ol li:nth-child(odd) {
  background-color: whitesmoke;
}

ol li:nth-child(even) {
  background-color: white;
}

li {
  display: inline-block;
  margin: 0 10px 10px 10px;
}

a {
  color: #42b983;
}
</style>
